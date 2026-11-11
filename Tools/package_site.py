"""Put the website and the web version together for Netlify: Builds/Site (to look at) and Builds/Rotorcraft-Site.zip
(to drop on Netlify). Also zips the Windows build as Builds/Rotorcraft-Windows.zip.

    F:/Blender/5.2/python/bin/python.exe -I Tools/package_site.py [--download-url URL | --bundle-windows] [--no-windows-zip]

Site layout (everything flat at the top, as the web zip always was):
    index.html, privacy.html, site.css, site.js, img/, fonts/, _redirects, _headers   from Website/
    play.html                                           the web build's index.html, loading the game from beside it
    Web.loader.js, Web.framework.js.unityweb, ...       the web build's Build/ files
    media/rotorcraft-trailer.mp4                        the 720p trailer
The game must stay at the top level: Unity keys a web player's saves to the folder its page is served from, so
/play (no trailing slash) keeps every existing browser career, where /play/ would lose them.

The Windows download is large (about 130 MB). By default the site links to it as a GitHub release asset
(free to serve, no size worries); --bundle-windows puts it in the site under downloads/ instead.
"""
import argparse, os, re, shutil, struct, sys, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))          # Rotorcraft-Unity
BUILDS = os.path.join(os.path.dirname(ROOT), "Builds")
SITE_SRC = os.path.join(ROOT, "Website")
WEB = os.path.join(BUILDS, "Web")
WINDOWS = os.path.join(BUILDS, "Windows")
TRAILER = os.path.join(BUILDS, "Trailer", "Rotorcraft-Trailer-720p-share.mp4")
OUT = os.path.join(BUILDS, "Site")
SITE_ZIP = os.path.join(BUILDS, "Rotorcraft-Site.zip")
WIN_ZIP = os.path.join(BUILDS, "Rotorcraft-Windows.zip")
DEFAULT_DOWNLOAD = "https://github.com/ddavies5824/Rotorcraft-Releases/releases/latest/download/Rotorcraft-Windows.zip"


def mb(n):
    return str(round(n / 1048576))


def mp4_length(path):
    """Minutes:seconds from the movie header (mvhd) of an MP4."""
    with open(path, "rb") as f:
        data = f.read(4 * 1048576)                                          # the index is at the front (faststart)
    i = data.find(b"mvhd")
    if i < 0:
        return None
    version = data[i + 4]
    if version == 1:
        scale, duration = struct.unpack(">IQ", data[i + 24:i + 36])
    else:
        scale, duration = struct.unpack(">II", data[i + 16:i + 24])
    s = round(duration / scale)
    return f"{s // 60}:{s % 60:02d}"


def zip_windows():
    tmp = WIN_ZIP + ".tmp"
    with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for root, dirs, files in os.walk(WINDOWS):
            dirs[:] = [d for d in dirs if "DoNotShip" not in d and "DontShip" not in d]
            for f in files:
                if f == "ONLINE-SETUP.md":                                  # for the developer, not the player
                    continue
                p = os.path.join(root, f)
                z.write(p, "Rotorcraft/" + os.path.relpath(p, WINDOWS).replace("\\", "/"))
    os.replace(tmp, WIN_ZIP)
    print("windows zip MiB", round(os.path.getsize(WIN_ZIP) / 1048576, 2))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--download-url", default=DEFAULT_DOWNLOAD)
    ap.add_argument("--bundle-windows", action="store_true", help="serve the Windows zip from the site itself")
    ap.add_argument("--no-windows-zip", action="store_true", help="use the Windows zip as it is")
    a = ap.parse_args()

    if not a.no_windows_zip or not os.path.exists(WIN_ZIP):
        zip_windows()

    if os.path.exists(OUT):
        shutil.rmtree(OUT)
    shutil.copytree(SITE_SRC, OUT)

    # the web version, flat at the top level, its page renamed play.html
    page = open(os.path.join(WEB, "index.html"), encoding="utf-8").read().replace('"Build/', '"')
    assert "Build/" not in page, "the game page still points into Build/"
    version = re.search(r'productVersion:\s*"([^"]+)"', page).group(1)
    open(os.path.join(OUT, "play.html"), "w", encoding="utf-8", newline="\n").write(page)
    web_bytes = 0
    for f in os.listdir(os.path.join(WEB, "Build")):
        shutil.copy2(os.path.join(WEB, "Build", f), os.path.join(OUT, f))
        web_bytes += os.path.getsize(os.path.join(WEB, "Build", f))
    for f in os.listdir(WEB):
        p = os.path.join(WEB, f)
        if os.path.isfile(p) and f not in ("index.html", "ONLINE-SETUP.md"):
            shutil.copy2(p, os.path.join(OUT, f))                           # logo, third-party notices

    os.makedirs(os.path.join(OUT, "media"), exist_ok=True)
    shutil.copy2(TRAILER, os.path.join(OUT, "media", "rotorcraft-trailer.mp4"))

    download = a.download_url
    if a.bundle_windows:
        os.makedirs(os.path.join(OUT, "downloads"), exist_ok=True)
        shutil.copy2(WIN_ZIP, os.path.join(OUT, "downloads", "Rotorcraft-Windows.zip"))
        download = "downloads/Rotorcraft-Windows.zip"

    fill = {
        "{{VERSION}}": version,
        "{{WIN_MB}}": mb(os.path.getsize(WIN_ZIP)),
        "{{WEB_MB}}": mb(web_bytes),
        "{{DOWNLOAD_URL}}": download,
        "{{TRAILER_URL}}": "media/rotorcraft-trailer.mp4",
        "{{TRAILER_MB}}": mb(os.path.getsize(TRAILER)),
        "{{TRAILER_LEN}}": mp4_length(TRAILER) or "2:44",
    }
    for name in ("index.html", "privacy.html"):
        p = os.path.join(OUT, name)
        html = open(p, encoding="utf-8").read()
        for k, v in fill.items():
            html = html.replace(k, v)
        left = re.findall(r"\{\{[A-Z_]+\}\}", html)
        assert not left, f"{name} still has {left}"
        open(p, "w", encoding="utf-8", newline="\n").write(html)

    tmp = SITE_ZIP + ".tmp"
    with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for root, dirs, files in os.walk(OUT):
            for f in files:
                p = os.path.join(root, f)
                z.write(p, os.path.relpath(p, OUT).replace("\\", "/"))      # forward slashes, or Netlify misreads it
    os.replace(tmp, SITE_ZIP)
    print("site", OUT)
    print("filled", {k.strip("{}"): v for k, v in fill.items()})
    print("site zip MiB", round(os.path.getsize(SITE_ZIP) / 1048576, 2))


if __name__ == "__main__":
    sys.exit(main())
