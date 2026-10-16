#!/bin/sh
# Headless E2E of the Linux build. Usage (host): tests/lab-linux.sh  — this part runs inside the client container.
export DOTNET_ROOT=/opt/dotnet PATH=/opt/dotnet:$PATH DISPLAY=:1 MAGICREMOTE_DATA=/tmp/data MAGICREMOTE_NOUPDATE=1
Xvfb :1 -screen 0 1920x1080x24 >/dev/null 2>&1 &
sleep 1
rm -rf /tmp/app && cp -r /dist /tmp/app
printf '#!/bin/sh\nexec chromium --no-sandbox --remote-debugging-port=9222 --disable-gpu "$@"\n' > /tmp/browser && chmod +x /tmp/browser
export MAGICREMOTE_BROWSER=/tmp/browser
dbus-run-session -- node /tests/e2e-linux.mjs; r1=$?
rm -rf /tmp/data
dbus-run-session -- node /tests/e2e-update.mjs; r2=$?
exit $((r1 | r2))
