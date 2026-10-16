#!/usr/bin/env python3
"""
Pravi psvim-<verzija>.msi iz foldera module/PsVim.

MSI je za trenutnog korisnika (bez administratorskih prava) i ne pokreće nikakve skripte:
samo kopira modul u oba korisnička foldera za PowerShell module:
  Documents\\WindowsPowerShell\\Modules\\PsVim   (Windows PowerShell 5.1)
  Documents\\PowerShell\\Modules\\PsVim          (PowerShell 7)
Posle instalacije `psvim` radi odmah (PowerShell sam učitava modul);
vim mod u promptu se uključuje komandom Set-PsVimProfile.

Potrebno: wixl (paket msitools / wixl na Linuxu).
Upotreba: python3 installer/make-msi.py [--out dist]
"""
import os, re, subprocess, sys, uuid, argparse
from xml.sax.saxutils import escape

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODULE = os.path.join(ROOT, "module", "PsVim")
NS = uuid.UUID("6d0b1c2e-3a41-4f7e-9a51-6c2f2b7f9e10")
UPGRADE_CODE = "{4F2A8C61-7B3E-4D59-9C1A-2E5B7D3F8A90}"


def guid(name):
    return "{" + str(uuid.uuid5(NS, name)).upper() + "}"


def ident(*parts):
    s = "_".join(parts)
    return re.sub(r"[^A-Za-z0-9_]", "_", s)[:70]


def version():
    psd = open(os.path.join(MODULE, "PsVim.psd1"), encoding="utf-8-sig").read()
    return re.search(r"ModuleVersion\s*=\s*'([\d.]+)'", psd).group(1)


def tree(rel=""):
    """Vraća (fajlovi, podfolderi) za folder modula."""
    full = os.path.join(MODULE, rel)
    files = sorted(f for f in os.listdir(full) if os.path.isfile(os.path.join(full, f)) and not f.endswith(".psvim-old"))
    dirs = sorted(d for d in os.listdir(full) if os.path.isdir(os.path.join(full, d)))
    return files, dirs


def emit_dir(root_key, rel, name, out, comps, depth):
    did = ident(root_key, "dir", rel or "root")
    ind = "  " * depth
    out.append(f'{ind}<Directory Id="{did}" Name="{escape(name)}">')
    files, dirs = tree(rel)
    cid = ident(root_key, "cmp", rel or "root")
    comps.append(cid)
    out.append(f'{ind}  <Component Id="{cid}" Guid="{guid(cid)}">')
    out.append(f'{ind}    <RegistryValue Root="HKCU" Key="Software\\psvim\\components" Name="{cid}" Type="integer" Value="1" KeyPath="yes"/>')
    out.append(f'{ind}    <RemoveFolder Id="rm_{cid}" On="uninstall"/>')
    for f in files:
        src = os.path.join(MODULE, rel, f)
        fid = ident(root_key, "f", rel, f)
        out.append(f'{ind}    <File Id="{fid}" Name="{escape(f)}" Source="{escape(src)}"/>')
    out.append(f'{ind}  </Component>')
    for d in dirs:
        emit_dir(root_key, os.path.join(rel, d) if rel else d, d, out, comps, depth + 1)
    out.append(f'{ind}</Directory>')


def make_wxs(ver):
    out, comps = [], []
    for key, parent in (("wps", "WindowsPowerShell"), ("ps7", "PowerShell")):
        out.append(f'        <Directory Id="{key}_parent" Name="{parent}">')
        out.append(f'          <Directory Id="{key}_modules" Name="Modules">')
        sub = []
        emit_dir(key, "", "PsVim", sub, comps, 6)
        out.extend(sub)
        out.append('          </Directory>')
        out.append('        </Directory>')
    # komponente za prazne roditeljske foldere (da se uklone pri deinstalaciji ako su prazni)
    extra = []
    for key in ("wps", "ps7"):
        for d in (f"{key}_parent", f"{key}_modules"):
            cid = ident("cmp", d)
            comps.append(cid)
            extra.append(f'''    <DirectoryRef Id="{d}">
      <Component Id="{cid}" Guid="{guid(cid)}">
        <RegistryValue Root="HKCU" Key="Software\\psvim\\components" Name="{cid}" Type="integer" Value="1" KeyPath="yes"/>
        <RemoveFolder Id="rm_{cid}" On="uninstall"/>
      </Component>
    </DirectoryRef>''')
    refs = "\n".join(f'      <ComponentRef Id="{c}"/>' for c in comps)
    dirs = "\n".join(out)
    extras = "\n".join(extra)
    return f'''<?xml version="1.0" encoding="utf-8"?>
<Wix xmlns="http://schemas.microsoft.com/wix/2006/wi">
  <Product Id="*" Name="psvim {ver}" Language="1033" Codepage="1252" Version="{ver}"
           Manufacturer="Vladimir Stojadinov" UpgradeCode="{UPGRADE_CODE}">
    <Package InstallerVersion="500" Compressed="yes" InstallScope="perUser"
             Description="psvim - vim and LazyVim for PowerShell"
             Comments="Installs the PsVim PowerShell module for the current user (PowerShell 5.1 and 7)."/>
    <MajorUpgrade DowngradeErrorMessage="A newer version of psvim is already installed."/>
    <Media Id="1" Cabinet="psvim.cab" EmbedCab="yes"/>
    <Property Id="ARPURLINFOABOUT" Value="https://github.com/vladimir-stojadinov/psvim"/>
    <Property Id="ARPHELPLINK" Value="https://github.com/vladimir-stojadinov/psvim/blob/main/UPUTSTVO.md"/>
    <Property Id="ARPNOMODIFY" Value="1"/>
    <Directory Id="TARGETDIR" Name="SourceDir">
      <Directory Id="PersonalFolder">
{dirs}
      </Directory>
    </Directory>
{extras}
    <Feature Id="Main" Title="psvim" Level="1">
{refs}
    </Feature>
  </Product>
</Wix>
'''


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(ROOT, "dist"))
    a = ap.parse_args()
    ver = version()
    os.makedirs(a.out, exist_ok=True)
    wxs = os.path.join(a.out, f"psvim-{ver}.wxs")
    msi = os.path.join(a.out, f"psvim-{ver}.msi")
    open(wxs, "w", encoding="utf-8").write(make_wxs(ver))
    subprocess.run(["wixl", "-v", "-o", msi, wxs], check=True)
    print(msi)


if __name__ == "__main__":
    main()
