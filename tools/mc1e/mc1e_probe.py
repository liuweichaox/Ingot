#!/usr/bin/env python3
"""
FX3U-ENET-ADP / 三菱 MC 协议 1E 帧（二进制）读取探针。

用途：在你们厂内，用能访问 FX3U 的机器直接对 PLC 发一帧"字批量读取"（命令 0x01），
      打印请求与响应的原始字节 + 解码结果，用于检查网络、端口和 PLC 的二进制通信设置。

无第三方依赖，仅标准库 socket。可在 Windows/Linux 上跑（能连到 PLC 的网络即可）。

用法：
  python mc1e_probe.py --host 192.168.1.10 --port 5551 --device D100 --count 4
说明：
  1E 帧字批量读取（命令 0x01）请求，二进制：
    [0]   子标题/命令 = 0x01（字单位批量读取，FX3U-ENET-ADP 手册 §Batch Read In Word Units, Command:01 已确认）
    [1]   PLC 号 = 0xFF
    [2-3] 监视定时器（小端，单位 250ms）
    [4-7] 起始软元件号（小端 4 字节，二进制）
    [8-9] 软元件代码（低字节到高字节，如 D 为 20H,44H）
    [10]  读取点数（字数；0 表示 256）
    [11]  固定 0x00
  响应（成功）：[0]=0x81(=0x01|0x80) [1]=结束码0x00，其后为 数据点数×2 字节的字数据（小端）。
                失败：[1]!=0x00，通常再跟 1 字节异常码。
"""
import argparse, socket, struct, sys, re

DEVICE_CODES = {  # 2 字节设备代码，1E 二进制按低字节到高字节发送
    "D": b" D", "W": b" W", "R": b" R", "M": b" M", "X": b" X", "Y": b" Y",
    "B": b" B", "T": b" T", "C": b" C", "L": b" L", "S": b" S",
}

def parse_device(dev):
    m = re.match(r"^([A-Za-z]+)(\d+)$", dev.strip())
    if not m:
        sys.exit(f"设备格式错误：{dev}（应形如 D100）")
    code = m.group(1).upper()
    if code not in DEVICE_CODES:
        sys.exit(f"未知设备代码：{code}，支持：{','.join(DEVICE_CODES)}")
    return DEVICE_CODES[code], int(m.group(2))

def build_1e_word_read(dev, count, timer=0x0010):
    code, addr = parse_device(dev)                 # code=2字节ASCII, addr=起始号
    pts = 0 if count == 256 else count             # 0 表示 256
    head = struct.pack("<I", addr)                 # 起始软元件号，小端4字节
    timer_b = struct.pack("<H", timer)             # 监视定时器，小端2字节
    body = head + code                             # 号 → 代码
    return bytes([0x01, 0xFF]) + timer_b + body + bytes([pts & 0xFF, 0x00])

def decode_response(resp, count):
    if len(resp) < 2:
        return f"响应过短：{resp.hex(' ')}"
    sub, end = resp[0], resp[1]
    head = f"子标题=0x{sub:02X} 结束码=0x{end:02X}"
    if end != 0x00:
        abn = resp[2] if len(resp) > 2 else None
        return f"{head}  ❌ PLC 返回错误" + (f"（异常码=0x{abn:02X}）" if abn is not None else "")
    data = resp[2:2 + count * 2]
    words = [struct.unpack_from("<H", data, i)[0] for i in range(0, len(data), 2)]
    signed = [w - 0x10000 if w >= 0x8000 else w for w in words]
    return (f"{head}  ✅ 成功\n  原始字(小端): " + " ".join(f"{w:5d}(0x{w:04X})" for w in words)
            + "\n  作有符号 int16: " + " ".join(str(s) for s in signed))

def main():
    ap = argparse.ArgumentParser(description="FX3U MC 1E 帧字读取探针")
    ap.add_argument("--host", required=True)
    ap.add_argument("--port", type=int, required=True, help="FX3U-ENET-ADP 的 MC 端口（常见 5551，以你现场配置为准）")
    ap.add_argument("--device", default="D100")
    ap.add_argument("--count", type=int, default=4)
    ap.add_argument("--timeout", type=float, default=3.0)
    a = ap.parse_args()

    req = build_1e_word_read(a.device, a.count)
    print(f">> 目标 {a.host}:{a.port}  读取 {a.device} 起 {a.count} 字")
    print(f">> 请求帧 ({len(req)}B): {req.hex(' ')}")
    try:
        with socket.create_connection((a.host, a.port), timeout=a.timeout) as s:
            s.sendall(req)
            resp = s.recv(2048)
    except Exception as e:
        sys.exit(f"通信失败：{e}\n  排查：端口对不对（MC 端口≠MELSOFT端口）、防火墙、FX3U-ENET-ADP 是否已配 MC 协议。")
    print(f"<< 响应帧 ({len(resp)}B): {resp.hex(' ')}")
    print(decode_response(resp, a.count))

if __name__ == "__main__":
    main()
