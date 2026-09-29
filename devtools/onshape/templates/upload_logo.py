"""Upload a logo PNG as a new blob tab in the template document (never replaces an existing tab).
usage: PYTHONPATH=. python devtools/onshape/templates/upload_logo.py <png> "<tab name>" """
import sys
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from _common import TD, TW, c, elements  # noqa: E402

png, name = Path(sys.argv[1]), sys.argv[2]
if any(e["name"] == name for e in elements(TD, TW)):
    raise SystemExit(f"tab '{name}' already exists")
b = uuid.uuid4().hex
body = (f"--{b}\r\nContent-Disposition: form-data; name=\"encodedFilename\"\r\n\r\n{name}\r\n"
        f"--{b}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{name}\"\r\n"
        f"Content-Type: image/png\r\n\r\n").encode() + png.read_bytes() + f"\r\n--{b}--\r\n".encode()
path = f"/api/v6/blobelements/d/{TD}/w/{TW}"
ct = f"multipart/form-data; boundary={b}"
h = c.auth.get_headers(method="POST", path=path, query_params=None, content_type=ct)
r = c.session.post(c.auth.get_full_url(path, None), headers=h, data=body, timeout=120)
print(r.status_code, r.text[:300])
