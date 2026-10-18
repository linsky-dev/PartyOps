# 本轮发布辅助入口

签名由主控显式执行 `sign-verified-manifest.py --key <原Ed25519私钥路径>`。入口重新执行 `checked_preparation`，精确核对已保存输入与 `compose`，验证历史九包签名，再读取明确路径一次。只增加新 Windows asset 和总清单签名，独占保存本轮 MANIFEST，并用 `signed_manifest` 复核。已有目标文件时阻断；不搜索、复制或打印私钥。实现者未读取实际私钥或执行签名。

WorkBuddy 入口为 `workbuddy-582-preflight.py --workspace <主控在官方UI已核对的工作区绝对路径>`，可加 `--discover-only`。不猜工作区；仅在内存将旧桥三个版本判断收窄为 5.8.2.0，保留 Tencent 有效签名、固定官方路径、上下文工作区与工具授权、唯一官方 listener 校验，旧桥不修改。允许 discover、initialize、notifications/initialized、tools/list；不调用 tools/call，不部署，不输出 headers、tokens 或上下文信封。连接器缺失或不匹配立即报告，不循环尝试。

离线定向测试：`test_sign_verified_manifest.py` 3 项、`test_workbuddy_582_preflight.py` 3 项通过。签名测试在实际私钥读取和签名前停止；连接器测试使用模拟 listener，未声称真实预检通过。实际官方 UI/工作区确认及后续执行由主控负责。
