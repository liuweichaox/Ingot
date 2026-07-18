use futures_util::StreamExt;
use reqwest::{Client, Method, Url};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use sha2::{Digest, Sha256};
use std::{io::Write, net::IpAddr, path::Path, time::Duration};
use tauri::{Emitter, Window};
use tempfile::NamedTempFile;

const MAX_API_RESPONSE_BYTES: usize = 16 * 1024 * 1024;
const MAX_SSE_FRAME_BYTES: usize = 1024 * 1024;
const MAX_PACKAGE_BYTES: u64 = 256 * 1024 * 1024;

#[derive(Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
struct ApiConnection {
    central_url: String,
    actor: String,
    token: String,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct ApiRequest {
    central_url: String,
    actor: String,
    token: String,
    method: String,
    path: String,
    body: Option<Value>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ApiResponse {
    status: u16,
    body: Value,
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct StreamEnvelope {
    run_id: String,
    sequence: Option<u64>,
    event_type: String,
    data: Value,
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct StreamClosed {
    run_id: String,
}

fn client(streaming: bool) -> Result<Client, String> {
    let mut builder = Client::builder()
        .connect_timeout(Duration::from_secs(10))
        .redirect(reqwest::redirect::Policy::none())
        .user_agent(concat!("Ingot-Agent/", env!("CARGO_PKG_VERSION")));
    if !streaming {
        builder = builder.timeout(Duration::from_secs(90));
    }
    builder
        .build()
        .map_err(|error| format!("无法初始化网络客户端：{error}"))
}

fn base_url(value: &str) -> Result<Url, String> {
    let mut parsed = Url::parse(value.trim()).map_err(|_| "Central API 地址无效。".to_string())?;
    if !matches!(parsed.scheme(), "http" | "https") || parsed.host_str().is_none() {
        return Err("Central API 地址必须使用 http 或 https。".to_string());
    }
    let host = parsed.host_str().expect("host was checked");
    let normalized_host = host.trim_start_matches('[').trim_end_matches(']');
    let loopback = normalized_host.eq_ignore_ascii_case("localhost")
        || normalized_host
            .parse::<IpAddr>()
            .is_ok_and(|address| address.is_loopback());
    if parsed.scheme() != "https" && !loopback {
        return Err("非回环 Central API 地址必须使用 HTTPS。".to_string());
    }
    if !parsed.username().is_empty() || parsed.password().is_some() || parsed.fragment().is_some() {
        return Err("Central API 地址不得包含凭据或片段。".to_string());
    }
    parsed.set_query(None);
    parsed.set_fragment(None);
    parsed.set_path("/");
    Ok(parsed)
}

fn valid_id(value: &str) -> bool {
    !value.is_empty()
        && value.len() <= 128
        && value
            .bytes()
            .all(|byte| byte.is_ascii_alphanumeric() || matches!(byte, b'-' | b'_'))
}

fn split_segments(path: &str) -> Option<Vec<&str>> {
    if path.len() > 4096 {
        return None;
    }
    let path_only = path.split('?').next()?;
    if !path.starts_with('/') || path.contains("..") || path.contains('\\') || path.contains('#') {
        return None;
    }
    Some(path_only.trim_matches('/').split('/').collect())
}

fn route_allowed(method: &Method, path: &str) -> bool {
    let Some(parts) = split_segments(path) else {
        return false;
    };
    match parts.as_slice() {
        ["api", "v1", "agent", "capabilities"] => method.as_str() == "GET",
        ["api", "v1", "agent", "artifacts"] => method.as_str() == "GET",
        ["api", "v1", "agent", "artifacts", id] => method.as_str() == "GET" && valid_id(id),
        ["api", "v1", "agent", "runs"] => matches!(method.as_str(), "GET" | "POST"),
        ["api", "v1", "agent", "runs", id] if method.as_str() == "GET" => valid_id(id),
        ["api", "v1", "agent", "runs", action] if method.as_str() == "POST" => {
            action.strip_suffix(":cancel").is_some_and(valid_id)
        }
        ["api", "v1", "connector-workspaces", action] if method.as_str() == "POST" => action
            .strip_suffix(":approve-package")
            .or_else(|| action.strip_suffix(":package"))
            .is_some_and(valid_id),
        ["api", "v1", "connector-workspaces", id] if method.as_str() == "GET" => valid_id(id),
        ["api", "v1", "connector-workspaces", id, "files"] => {
            method.as_str() == "GET" && valid_id(id)
        }
        ["api", "v1", "connector-workspaces", id, "file"] => {
            method.as_str() == "GET" && valid_id(id)
        }
        _ => false,
    }
}

fn api_url(central_url: &str, path: &str, method: &Method) -> Result<Url, String> {
    if !route_allowed(method, path) {
        return Err("桌面应用拒绝访问非连接器工程 API。".to_string());
    }
    base_url(central_url)?
        .join(path.trim_start_matches('/'))
        .map_err(|_| "API 路径无效。".to_string())
}

fn authenticated_request(
    client: &Client,
    method: Method,
    url: Url,
    connection: &ApiConnection,
) -> Result<reqwest::RequestBuilder, String> {
    let actor = connection.actor.trim();
    let token = connection.token.trim();
    if actor.is_empty() || actor.len() > 128 {
        return Err("Actor 无效。".to_string());
    }
    if token.is_empty() {
        return Err("必须提供 Agent 访问令牌。".to_string());
    }
    Ok(client
        .request(method, url)
        .header("X-Ingot-Actor", actor)
        .header("X-Ingot-Client", "ingot-agent-desktop")
        .bearer_auth(token))
}

#[tauri::command]
async fn api_request(request: ApiRequest) -> Result<ApiResponse, String> {
    let method = Method::from_bytes(request.method.as_bytes())
        .map_err(|_| "仅支持 GET 或 POST。".to_string())?;
    if !matches!(method, Method::GET | Method::POST) {
        return Err("仅支持 GET 或 POST。".to_string());
    }
    let connection = ApiConnection {
        central_url: request.central_url,
        actor: request.actor,
        token: request.token,
    };
    let url = api_url(&connection.central_url, &request.path, &method)?;
    let network = client(false)?;
    let mut builder = authenticated_request(&network, method, url, &connection)?;
    if let Some(body) = request.body {
        builder = builder.json(&body);
    }
    let response = builder
        .send()
        .await
        .map_err(|error| format!("Central API 请求失败：{error}"))?;
    let status = response.status().as_u16();
    if response
        .content_length()
        .is_some_and(|size| size > MAX_API_RESPONSE_BYTES as u64)
    {
        return Err("Central API 响应超过桌面应用限制。".to_string());
    }
    let bytes = response
        .bytes()
        .await
        .map_err(|error| format!("读取 Central API 响应失败：{error}"))?;
    if bytes.len() > MAX_API_RESPONSE_BYTES {
        return Err("Central API 响应超过桌面应用限制。".to_string());
    }
    let body = if bytes.is_empty() {
        Value::Null
    } else {
        serde_json::from_slice(&bytes)
            .unwrap_or_else(|_| Value::String(String::from_utf8_lossy(&bytes).into_owned()))
    };
    Ok(ApiResponse { status, body })
}

#[tauri::command]
async fn stream_agent_run(
    window: Window,
    connection: ApiConnection,
    run_id: String,
    after_sequence: Option<u64>,
) -> Result<(), String> {
    if !valid_id(&run_id) {
        return Err("Run ID 无效。".to_string());
    }
    let path = format!("/api/v1/agent/runs/{run_id}/stream");
    let url = base_url(&connection.central_url)?
        .join(path.trim_start_matches('/'))
        .map_err(|_| "运行流地址无效。".to_string())?;
    let network = client(true)?;
    let mut request = authenticated_request(&network, Method::GET, url, &connection)?;
    if let Some(sequence) = after_sequence.filter(|value| *value > 0) {
        request = request.header("Last-Event-ID", sequence.to_string());
    }
    let response = request
        .send()
        .await
        .map_err(|error| format!("无法连接运行事件流：{error}"))?;
    if !response.status().is_success() {
        let status = response.status();
        let message = response.text().await.unwrap_or_default();
        return Err(format!("运行事件流返回 {status}：{message}"));
    }

    let mut stream = response.bytes_stream();
    let mut buffer = String::new();
    while let Some(chunk) = stream.next().await {
        let chunk = chunk.map_err(|error| format!("运行事件流中断：{error}"))?;
        buffer.push_str(&String::from_utf8_lossy(&chunk));
        if buffer.len() > MAX_SSE_FRAME_BYTES && frame_boundary(&buffer).is_none() {
            return Err("运行事件超过 1 MiB 限制。".to_string());
        }
        while let Some((boundary, delimiter_length)) = frame_boundary(&buffer) {
            if boundary > MAX_SSE_FRAME_BYTES {
                return Err("运行事件超过 1 MiB 限制。".to_string());
            }
            let frame = buffer[..boundary].to_string();
            buffer.drain(..boundary + delimiter_length);
            if let Some(event) = parse_event_frame(&run_id, &frame) {
                window
                    .emit("agent-stream", event)
                    .map_err(|error| format!("无法发布运行事件：{error}"))?;
            }
        }
    }
    window
        .emit("agent-stream-closed", StreamClosed { run_id })
        .map_err(|error| format!("无法发布运行结束事件：{error}"))?;
    Ok(())
}

fn frame_boundary(buffer: &str) -> Option<(usize, usize)> {
    match (buffer.find("\n\n"), buffer.find("\r\n\r\n")) {
        (Some(lf), Some(crlf)) if lf <= crlf => Some((lf, 2)),
        (Some(_), Some(crlf)) => Some((crlf, 4)),
        (Some(lf), None) => Some((lf, 2)),
        (None, Some(crlf)) => Some((crlf, 4)),
        (None, None) => None,
    }
}

fn parse_event_frame(run_id: &str, frame: &str) -> Option<StreamEnvelope> {
    let mut sequence = None;
    let mut event_type = "message".to_string();
    let mut data_lines = Vec::new();
    for line in frame.lines() {
        let line = line.trim_end_matches('\r');
        if let Some(value) = line.strip_prefix("id:") {
            sequence = value.trim().parse().ok();
        } else if let Some(value) = line.strip_prefix("event:") {
            event_type = value.trim().to_string();
        } else if let Some(value) = line.strip_prefix("data:") {
            data_lines.push(value.trim_start());
        }
    }
    if data_lines.is_empty() {
        return None;
    }
    let raw = data_lines.join("\n");
    let data = serde_json::from_str(&raw).unwrap_or(Value::String(raw));
    if sequence.is_none() {
        sequence = data.get("sequence").and_then(Value::as_u64);
    }
    Some(StreamEnvelope {
        run_id: run_id.to_string(),
        sequence,
        event_type,
        data,
    })
}

#[tauri::command]
async fn download_package(
    connection: ApiConnection,
    workspace_id: String,
    target_path: String,
    expected_sha256: String,
) -> Result<String, String> {
    if !valid_id(&workspace_id) {
        return Err("Workspace ID 无效。".to_string());
    }
    let expected = expected_sha256.trim().to_ascii_lowercase();
    if expected.len() != 64 || !expected.bytes().all(|byte| byte.is_ascii_hexdigit()) {
        return Err("预期 SHA-256 无效。".to_string());
    }
    let target = Path::new(target_path.trim());
    if target.as_os_str().is_empty() || target.file_name().is_none() {
        return Err("保存路径无效。".to_string());
    }
    if target.extension().and_then(|value| value.to_str()) != Some("zip") {
        return Err("连接器包必须保存为 .zip 文件。".to_string());
    }
    let path = format!("/api/v1/connector-workspaces/{workspace_id}/package");
    let url = base_url(&connection.central_url)?
        .join(path.trim_start_matches('/'))
        .map_err(|_| "下载地址无效。".to_string())?;
    let network = client(false)?;
    let response = authenticated_request(&network, Method::GET, url, &connection)?
        .send()
        .await
        .map_err(|error| format!("下载连接器包失败：{error}"))?;
    if !response.status().is_success() {
        let status = response.status();
        let message = response.text().await.unwrap_or_default();
        return Err(format!("连接器包下载返回 {status}：{message}"));
    }
    if response
        .content_length()
        .is_some_and(|size| size > MAX_PACKAGE_BYTES)
    {
        return Err("连接器包超过 256 MiB 下载限制。".to_string());
    }
    let bytes = response
        .bytes()
        .await
        .map_err(|error| format!("读取连接器包失败：{error}"))?;
    if bytes.len() as u64 > MAX_PACKAGE_BYTES {
        return Err("连接器包超过 256 MiB 下载限制。".to_string());
    }
    let actual = hex::encode(Sha256::digest(&bytes));
    if actual != expected {
        return Err(format!(
            "连接器包 SHA-256 校验失败：期望 {expected}，实际 {actual}。"
        ));
    }
    let parent = target
        .parent()
        .filter(|path| !path.as_os_str().is_empty())
        .unwrap_or(Path::new("."));
    let mut temporary = NamedTempFile::new_in(parent)
        .map_err(|error| format!("无法在目标目录创建临时文件：{error}"))?;
    temporary
        .write_all(&bytes)
        .and_then(|_| temporary.as_file().sync_all())
        .map_err(|error| format!("写入连接器包临时文件失败：{error}"))?;
    temporary.persist(target).map_err(|error| {
        let message = format!("原子保存连接器包失败：{}", error.error);
        drop(error.file);
        message
    })?;
    Ok(target.to_string_lossy().into_owned())
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .invoke_handler(tauri::generate_handler![
            api_request,
            stream_agent_run,
            download_package
        ])
        .run(tauri::generate_context!())
        .expect("error while running Ingot Agent");
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn allows_only_connector_engineering_routes() {
        assert!(route_allowed(&Method::POST, "/api/v1/agent/runs"));
        assert!(route_allowed(&Method::GET, "/api/v1/agent/runs?limit=12"));
        assert!(route_allowed(
            &Method::POST,
            "/api/v1/connector-workspaces/abc123:approve-package"
        ));
        assert!(route_allowed(
            &Method::GET,
            "/api/v1/connector-workspaces/abc123/file?path=Program.cs"
        ));
        assert!(!route_allowed(&Method::GET, "/api/v1/events"));
        assert!(!route_allowed(&Method::POST, "/api/v1/configuration"));
        assert!(!route_allowed(
            &Method::GET,
            "/api/v1/connector-workspaces/../secrets"
        ));
    }

    #[test]
    fn parses_server_sent_events() {
        let frame =
            "id: 12\nevent: tool.completed\ndata: {\"sequence\":12,\"type\":\"tool.completed\"}";
        let parsed = parse_event_frame("run-1", frame).expect("event");
        assert_eq!(parsed.sequence, Some(12));
        assert_eq!(parsed.event_type, "tool.completed");
    }

    #[test]
    fn permits_http_only_for_loopback_addresses() {
        assert!(base_url("http://127.0.0.1:8000").is_ok());
        assert!(base_url("http://[::1]:8000").is_ok());
        assert!(base_url("http://localhost:8000").is_ok());
        assert!(base_url("https://central.example.com").is_ok());
        assert!(base_url("http://central.example.com").is_err());
    }
}
