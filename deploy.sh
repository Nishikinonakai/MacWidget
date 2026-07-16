#!/bin/bash
# 构建 + 部署到 home-win。用法: ./deploy.sh [home-win 的当前 IP]
# IP 会漂（DHCP）：连不上先扫 curl :18800/ping，见 TESTPLAN.md。
set -euo pipefail
cd "$(dirname "$0")"
IP="${1:-192.168.1.8}"

# NuGet 直连（Clash 代理会掐 api.nuget.org 的 TLS 握手）
env -u HTTP_PROXY -u HTTPS_PROXY -u http_proxy -u https_proxy -u ALL_PROXY -u all_proxy \
  ~/.dotnet/dotnet publish src/WidgetProto -c Release -r win-x64 --no-self-contained -o publish

ssh "nakai@$IP" "cmd /c (if not exist C:\\work\\widgetproto\\app mkdir C:\\work\\widgetproto\\app) & (if not exist C:\\work\\widgetproto\\tools mkdir C:\\work\\widgetproto\\tools)"
# 目标机上可能还在跑：先杀（否则文件锁让 scp Failure）
ssh "nakai@$IP" "cmd /c taskkill /f /im WidgetProto.exe 2>nul & exit /b 0"
scp -r publish/* "nakai@$IP:C:/work/widgetproto/app/"
scp tools/*.ps1 "nakai@$IP:C:/work/widgetproto/tools/"
echo "OK -> $IP C:\\work\\widgetproto"
