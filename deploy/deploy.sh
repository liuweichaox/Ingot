#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="${SCRIPT_DIR}/compose.yml"
RUNTIME_DIR="${SCRIPT_DIR}/runtime"
STATE_FILE="${RUNTIME_DIR}/target"

SITE_DOMAIN="${SITE_DOMAIN:-ingotstack.com}"
DOCS_DOMAIN="${DOCS_DOMAIN:-docs.ingotstack.com}"
ACME_EMAIL="${ACME_EMAIL:-}"

usage() {
  cat <<'EOF'
用法：
  ./deploy/deploy.sh site              只部署官网
  ./deploy/deploy.sh docs              只部署文档
  ./deploy/deploy.sh all               部署官网和文档
  ./deploy/deploy.sh status            查看状态
  ./deploy/deploy.sh logs [服务名]     查看日志
  ./deploy/deploy.sh stop              停止服务（保留证书卷）
  ./deploy/deploy.sh backup [目录]     备份 Caddy 证书与配置卷

可选环境变量：SITE_DOMAIN、DOCS_DOMAIN、ACME_EMAIL。
EOF
}

require_docker() {
  if ! command -v docker >/dev/null 2>&1; then
    echo "未找到 Docker。Ubuntu 请先运行：sudo ./deploy/install-docker-ubuntu.sh" >&2
    exit 1
  fi
  if ! docker compose version >/dev/null 2>&1; then
    echo "未找到 Docker Compose v2 插件。" >&2
    exit 1
  fi
  if ! docker info >/dev/null 2>&1; then
    echo "当前用户无法连接 Docker daemon；请启动 Docker 或把用户加入 docker 组。" >&2
    exit 1
  fi
}

write_caddyfile() {
  local target="$1"
  local global_options=""
  mkdir -p "${RUNTIME_DIR}"

  if [[ -n "${ACME_EMAIL}" ]]; then
    global_options="{\n\temail ${ACME_EMAIL}\n}\n\n"
  fi

  {
    printf '%b' "${global_options}"
    if [[ "${target}" == "site" || "${target}" == "all" ]]; then
      cat <<EOF
www.${SITE_DOMAIN} {
	redir https://${SITE_DOMAIN}{uri} permanent
}

${SITE_DOMAIN} {
	encode zstd gzip
	reverse_proxy site:3000
	header {
		Strict-Transport-Security "max-age=31536000; includeSubDomains"
		-Server
	}
}

EOF
    fi
    if [[ "${target}" == "docs" || "${target}" == "all" ]]; then
      cat <<EOF
${DOCS_DOMAIN} {
	encode zstd gzip
	reverse_proxy docs:80
	header {
		Strict-Transport-Security "max-age=31536000; includeSubDomains"
		-Server
	}
}
EOF
    fi
  } > "${RUNTIME_DIR}/Caddyfile"
}

deploy_target() {
  local target="$1"
  local -a profiles=()

  case "${target}" in
    site) profiles=(--profile site) ;;
    docs) profiles=(--profile docs) ;;
    all) profiles=(--profile site --profile docs) ;;
    *) usage; exit 2 ;;
  esac

  write_caddyfile "${target}"
  docker compose -f "${COMPOSE_FILE}" config --quiet

  docker compose -f "${COMPOSE_FILE}" "${profiles[@]}" up -d --build --wait
  docker compose -f "${COMPOSE_FILE}" exec -T gateway caddy validate --config /etc/caddy/Caddyfile
  docker compose -f "${COMPOSE_FILE}" exec -T gateway caddy reload --config /etc/caddy/Caddyfile

  # 新目标健康且代理配置生效后，再停止不再公开的站点。
  if [[ "${target}" == "site" ]]; then
    docker compose -f "${COMPOSE_FILE}" --profile docs rm -f -s docs >/dev/null
  elif [[ "${target}" == "docs" ]]; then
    docker compose -f "${COMPOSE_FILE}" --profile site rm -f -s site >/dev/null
  fi

  printf '%s\n' "${target}" > "${STATE_FILE}"

  echo
  echo "部署完成：${target}"
  [[ "${target}" == "docs" ]] || echo "官网：https://${SITE_DOMAIN}"
  [[ "${target}" == "site" ]] || echo "文档：https://${DOCS_DOMAIN}"
}

backup_volumes() {
  local destination="${1:-${SCRIPT_DIR}/backups/$(date -u +%Y%m%dT%H%M%SZ)}"
  mkdir -p "${destination}"
  destination="$(cd -- "${destination}" && pwd)"
  docker run --rm \
    -v ingot-caddy-data:/source:ro \
    -v "${destination}:/backup" \
    alpine:3.22 tar -C /source -czf /backup/caddy-data.tar.gz .
  docker run --rm \
    -v ingot-caddy-config:/source:ro \
    -v "${destination}:/backup" \
    alpine:3.22 tar -C /source -czf /backup/caddy-config.tar.gz .
  cp "${RUNTIME_DIR}/Caddyfile" "${destination}/Caddyfile"
  echo "备份已写入：${destination}"
}

main() {
  local command="${1:-}"

  case "${command}" in
    -h|--help|help|"") usage; return ;;
  esac

  require_docker

  case "${command}" in
    site|docs|all) deploy_target "${command}" ;;
    status) docker compose -f "${COMPOSE_FILE}" --profile site --profile docs ps ;;
    logs)
      shift
      docker compose -f "${COMPOSE_FILE}" --profile site --profile docs logs -f --tail=200 "$@"
      ;;
    stop) docker compose -f "${COMPOSE_FILE}" --profile site --profile docs down ;;
    backup)
      shift
      backup_volumes "${1:-}"
      ;;
    *) usage; exit 2 ;;
  esac
}

main "$@"
