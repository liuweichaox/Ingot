#!/usr/bin/env bash
# 构建 + 测试，并把完整输出写进仓库根的 build-output.log。
# 目的：让隔着沙箱的 Claude 能读到真实的编译器/测试输出，逐个 fix-forward。
# 在有 .NET 10 SDK 的 WSL / Linux / macOS 里跑：
#   cd <仓库根>；export PATH="$HOME/.dotnet:$PATH"（若 dotnet 不在 PATH）；bash scripts/build-and-log.sh
set +e   # 故意不因失败中断——要的就是失败信息

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT" || exit 1
LOG="$ROOT/build-output.log"

{
  echo "===== Ingot build-and-log @ $(date -u +%Y-%m-%dT%H:%M:%SZ) ====="
  echo "pwd:   $ROOT"
  echo "uname: $(uname -sr 2>/dev/null)"
  if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: 找不到 dotnet。请先安装 .NET 10 SDK："
    echo "  wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh"
    echo "  bash /tmp/dotnet-install.sh --channel 10.0"
    echo "  export PATH=\"\$HOME/.dotnet:\$PATH\"   # 建议加进 ~/.bashrc"
    echo "然后重跑本脚本。"
  else
    echo "dotnet --version: $(dotnet --version 2>&1)"
    echo "dotnet --list-sdks:"
    dotnet --list-sdks 2>&1

    echo; echo "===== [1/3] restore ====="
    dotnet restore Ingot.sln 2>&1

    echo; echo "===== [2/3] build (Debug) ====="
    dotnet build Ingot.sln -c Debug --no-restore 2>&1

    echo; echo "===== [3/3] test ====="
    dotnet test tests/Ingot.Core.Tests/Ingot.Core.Tests.csproj -c Debug --no-restore 2>&1
  fi
  echo "===== 结束 ====="
} 2>&1 | tee "$LOG"

# 日志写完后再提炼错误摘要，追加到末尾（Claude 优先读这一段）
{
  echo
  echo "===== 错误/结果摘要 ====="
  grep -nE 'error (CS|MSB|NU|NETSDK)[0-9]+|Build FAILED|Build succeeded|Passed!|Failed!|error :' "$LOG" \
    || echo "（未匹配到 error/结果标记——可能是 SDK 缺失或提前退出，看上面完整输出）"
} | tee -a "$LOG"

echo
echo ">> 已生成 build-output.log（在仓库根）。跟 Claude 说一声，它会从这边读取并修复。"
echo ">> 提示：build-output.log 是临时产物，可加进 .gitignore，不必提交。"
