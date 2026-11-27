"""Update one release image in the server-owned env file without printing secrets."""
import os,re
from pathlib import Path
key=os.environ['RELEASE_IMAGE_KEY'];value=os.environ['RELEASE_IMAGE']
if key not in {'MONOLITH_IMAGE','TRIP_TRACKING_IMAGE','NOTIFICATION_IMAGE','GATEWAY_IMAGE'} or not re.fullmatch(r'[a-zA-Z0-9./:_@-]+',value):
    raise SystemExit('Invalid release image')
p=Path('.env.production')
if not p.is_file():raise SystemExit('Provision .env.production first')
lines=p.read_text().splitlines();updated=False
for i,line in enumerate(lines):
    if line.startswith(key+'='):lines[i]=key+'='+value;updated=True
if not updated:lines.append(key+'='+value)
tmp=p.with_name('.env.production.next');tmp.write_text('\n'.join(lines)+'\n');tmp.chmod(0o600);tmp.replace(p)
