#!/usr/bin/env python3
"""Pravi dist/psvim-<verzija>.msi i dist/psvim-<verzija>.zip (za GitHub Release)."""
import os, subprocess, zipfile, re, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
psd = open(os.path.join(ROOT, "module/PsVim/PsVim.psd1"), encoding="utf-8-sig").read()
ver = re.search(r"ModuleVersion\s*=\s*'([\d.]+)'", psd).group(1)
dist = os.path.join(ROOT, "dist"); os.makedirs(dist, exist_ok=True)
subprocess.run([sys.executable, os.path.join(ROOT, "installer/make-msi.py"), "--out", dist], check=True)
zpath = os.path.join(dist, f"psvim-{ver}.zip")
top = f"psvim-{ver}"
with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
    for f in ("setup.ps1", "README.md", "UPUTSTVO.md"):
        z.write(os.path.join(ROOT, f), f"{top}/{f}")
    for f in ("Install.cmd", "Uninstall.cmd"):
        z.write(os.path.join(ROOT, "installer", f), f"{top}/{f}")
    for base, _, files in os.walk(os.path.join(ROOT, "module")):
        for f in files:
            if ".psvim-old" in f: continue
            full = os.path.join(base, f)
            z.write(full, f"{top}/" + os.path.relpath(full, ROOT).replace(os.sep, "/"))
print(zpath)
