# psvim — instalacija

Ovo je detaljno uputstvo za instalaciju, nadogradnju i uklanjanje psvim-a na Windows-u. Za korišćenje editora pogledaj [UPUTSTVO.md](UPUTSTVO.md).

## Sadržaj

1. [Preduslovi](#1-preduslovi)
2. [Koji način instalacije da izaberem](#2-koji-način-instalacije-da-izaberem)
3. [Način A: MSI instaler](#3-način-a-msi-instaler-preporučeno)
4. [Način B: ZIP sa Install.cmd](#4-način-b-zip-sa-installcmd)
5. [Način C: jedna komanda](#5-način-c-jedna-komanda)
6. [Način D: ručno kopiranje modula](#6-način-d-ručno-kopiranje-modula)
7. [Način E: iz izvornog koda](#7-način-e-iz-izvornog-koda)
8. [Posle instalacije](#8-posle-instalacije)
9. [Nadogradnja](#9-nadogradnja)
10. [Uklanjanje](#10-uklanjanje)
11. [Rešavanje problema](#11-rešavanje-problema)

---

## 1. Preduslovi

psvim radi u oba PowerShell-a koja postoje na Windows-u:

| | Windows PowerShell 5.1 | PowerShell 7.4+ |
|---|---|---|
| Kako ga prepoznati | plava ikonica, `powershell.exe` | crna ikonica, `pwsh.exe` |
| Dolazi uz Windows | da (Windows 10 i 11) | ne, instalira se posebno |
| Potrebno još | .NET Framework 4.7.2+ (ima ga Windows 10 1803+ i Windows 11) | ništa |
| Predlozi u promptu | samo uz PSReadLine 2.1+ | da |

Svi načini instalacije osim D postavljaju psvim za oba PowerShell-a ako su oba instalirana.

Provera verzija:

```powershell
$PSVersionTable.PSVersion                                   # verzija PowerShell-a
(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full').Release   # 461808 ili više = 4.7.2+
Get-ExecutionPolicy -List                                    # da li su skripte dozvoljene (vidi 11.1)
```

Preporučuje se **Windows Terminal** (u Windows 11 je podrazumevan). Stari konzolni prozor radi, ali lošije prikazuje boje i posebne znakove.

---

## 2. Koji način instalacije da izaberem

| Situacija | Način |
|---|---|
| Običan računar, ili antivirus (npr. Cynet) blokira `irm … \| iex` | **A — MSI** |
| Želiš da te instalacija vodi kroz podešavanja (tema, prompt…) | **B — ZIP** |
| Sve je dozvoljeno i želiš najbrže | **C — jedna komanda** |
| Zaključan računar: ne smeš da pokrećeš instalere ni skripte | **D — ručno** |
| Razvijaš psvim ili želiš najnoviji kod | **E — izvorni kod** |

Nijedan način ne traži administratorska prava. Sve se instalira samo za tvog korisnika.

---

## 3. Način A: MSI instaler (preporučeno)

MSI samo kopira modul u korisničke foldere za PowerShell module. Ne pokreće nikakve skripte i ne menja sistemska podešavanja.

1. Preuzmi instaler:
   **https://github.com/vladimir-stojadinov/psvim/raw/main/releases/psvim-0.2.0.msi**
   (sve verzije: folder [releases](https://github.com/vladimir-stojadinov/psvim/tree/main/releases), SHA256 zbirovi u `SHA256SUMS.txt`)
2. Dvaput klikni na preuzeti fajl.
   - Ako se pojavi **„Windows protected your PC“**: klikni *More info → Run anyway*. Upozorenje se javlja jer instaler nije digitalno potpisan.
   - Ako antivirus i dalje blokira MSI, zamoli IT da doda izuzetak, ili koristi način D.
3. Instalacija traje nekoliko sekundi i nema dodatnih pitanja.
4. Otvori **novi** PowerShell prozor i ukucaj `psvim`.

Gde se instalira:

```
Documents\WindowsPowerShell\Modules\PsVim   ← Windows PowerShell 5.1
Documents\PowerShell\Modules\PsVim          ← PowerShell 7
```

Ako je folder Documents preusmeren na OneDrive, MSI koristi taj folder, isto kao i PowerShell.

**Tiha instalacija** (skripte, Intune, više računara), u kontekstu korisnika:

```powershell
msiexec /i psvim-0.2.0.msi /qn /l*v "$env:TEMP\psvim-install.log"
```

Zatim nastavi na [Posle instalacije](#8-posle-instalacije). Tamo se uključuje vim mod u promptu, jer MSI ne dira tvoj `$PROFILE`.

---

## 4. Način B: ZIP sa Install.cmd

1. Preuzmi **https://github.com/vladimir-stojadinov/psvim/raw/main/releases/psvim-0.2.0.zip**
2. Raspakuj (desni klik → *Extract All…*).
3. U raspakovanom folderu dvaput klikni **`Install.cmd`**.
4. Odgovori na pitanja. Enter prihvata podrazumevani odgovor (u zagradi je veliko slovo):
   - vim mod u PowerShell promptu (Esc + razmak = meni)
   - tema: tokyonight, catppuccin, gruvbox ili kanagawa
   - relativni brojevi linija, Nerd Font, uvlačenje 4 ili 2
   - PSScriptAnalyzer (upozorenja i formatiranje koda), lazygit
   - ako postoji i drugi PowerShell (5.1 ili 7), da li da instalira i za njega
5. Otvori novi PowerShell prozor i ukucaj `psvim`.

`Install.cmd` pokreće `setup.ps1` iz raspakovanog foldera, pa se ništa ne preuzima sa interneta. Pored modula, setup upisuje psvim u `$PROFILE` (prethodno pravi rezervnu kopiju `profile.ps1.psvim-bak`) i pravi konfiguraciju.

---

## 5. Način C: jedna komanda

U PowerShell-u (5.1 ili 7):

```powershell
irm https://raw.githubusercontent.com/vladimir-stojadinov/psvim/main/setup.ps1 | iex
```

Radi isto što i način B, samo preuzima sve sa GitHub-a. Neki antivirusi blokiraju ovaj oblik („preuzmi i izvrši“). Tada koristi A ili B.

Sa opcijama, bez pitanja:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/vladimir-stojadinov/psvim/main/setup.ps1))) -Yes -Theme gruvbox
```

Opcije za `setup.ps1` (važe i za `Install.cmd`, npr. `Install.cmd -Yes -NoPrompt`):

| Opcija | Šta radi |
|---|---|
| `-Yes` | bez pitanja, sve podrazumevano |
| `-Theme <ime>` | `tokyonight`, `catppuccin`, `gruvbox`, `kanagawa` |
| `-NoPrompt` | bez vim moda u promptu (samo editor) |
| `-NoAnalyzer` / `-NoLazygit` | preskoči dodatke |
| `-ResetConfig` | prepiši postojeću konfiguraciju |
| `-NoOtherEdition` | ne nudi instalaciju za drugi PowerShell |
| `-Build` | izgradi DLL-ove iz koda (treba .NET 8 SDK) |
| `-Uninstall` | ukloni modul i psvim blok iz `$PROFILE` |

---

## 6. Način D: ručno kopiranje modula

Za računare na kojima ne smeš da pokrećeš instalere ni skripte za instalaciju.

1. Preuzmi i raspakuj ZIP (način B, koraci 1 i 2).
2. Iskopiraj folder `module\PsVim` u:
   - `Documents\WindowsPowerShell\Modules\` za Windows PowerShell 5.1
   - `Documents\PowerShell\Modules\` za PowerShell 7

   Rezultat treba da bude npr. `Documents\WindowsPowerShell\Modules\PsVim\PsVim.psd1`.
3. **Odblokiraj fajlove.** Windows obeležava fajlove preuzete sa interneta, a Windows PowerShell 5.1 takav DLL odbija da učita:

   ```powershell
   $docs = [Environment]::GetFolderPath('MyDocuments')
   foreach ($d in "$docs\WindowsPowerShell\Modules\PsVim", "$docs\PowerShell\Modules\PsVim") {
       if (Test-Path $d) { Get-ChildItem $d -Recurse | Unblock-File }
   }
   ```

4. Otvori novi PowerShell prozor i ukucaj `psvim`.

---

## 7. Način E: iz izvornog koda

Potrebni su git i [.NET 8 SDK](https://dotnet.microsoft.com/download) (`winget install Microsoft.DotNet.SDK.8`).

```powershell
git clone https://github.com/vladimir-stojadinov/psvim
cd psvim
./build.ps1 -Test       # build za PowerShell 7 i 5.1 + testovi
./setup.ps1             # instalacija iz kloniranog foldera
```

Za pravljenje MSI i ZIP paketa (na Linuxu, treba `wixl`): `python3 installer/make-release.py`.

---

## 8. Posle instalacije

**Provera:**

```powershell
Get-Module -ListAvailable PsVim | Select-Object Name, Version, ModuleBase
psvim
```

U editoru `:checkhealth` pokazuje šta je dostupno (clipboard, Get-Help, PSScriptAnalyzer, git, lazygit).

**Vim mod u promptu i automatsko učitavanje** (posle MSI-ja ili ručne instalacije, jer način B/C to već radi):

```powershell
Set-PsVimProfile             # psvim + vim mod u promptu (Esc, pa razmak = meni)
Set-PsVimProfile -NoPrompt   # samo automatsko učitavanje, prompt ostaje kakav je
```

Komanda dodaje blok između `# >>> psvim >>>` i `# <<< psvim <<<` u `$PROFILE` i pravi rezervnu kopiju. Ostatak profila ne menja. Treba je pokrenuti posebno u Windows PowerShell-u 5.1 i u PowerShell-u 7, jer svaki ima svoj profil.

Bez profila `psvim` i dalje radi, jer se modul sam učita kad ukucaš komandu.

**Preporuke:**

| Šta | Zašto | Kako |
|---|---|---|
| Nerd Font | ikonice u stablu i statusnoj liniji | instaliraj npr. *JetBrainsMono Nerd Font*, izaberi ga u Windows Terminal-u, pa u editoru `:set nerdfont` (ili `"nerdFont": true` u konfiguraciji) |
| PSScriptAnalyzer | upozorenja pri snimanju, `␣cf` formatiranje | `Install-Module PSScriptAnalyzer -Scope CurrentUser` |
| lazygit | `␣gg` u editoru | `winget install JesseDuffield.lazygit` |
| PSReadLine 2.1+ (samo za 5.1) | predlozi dok kucaš u promptu | `Install-Module PSReadLine -Scope CurrentUser -Force` |

**Konfiguracija:** `Edit-PsVimConfig`, ili `␣fc` u editoru. Fajl je `%USERPROFILE%\.config\psvim\config.json` i zajednički je za oba PowerShell-a.

---

## 9. Nadogradnja

| Instalirano preko | Nadogradnja |
|---|---|
| MSI | pokreni noviji `.msi`; sam zamenjuje stari |
| ZIP | raspakuj noviji ZIP i pokreni `Install.cmd` (postojeća konfiguracija se zadržava) |
| jedna komanda | pokreni istu komandu ponovo |
| ručno | obriši stari folder `PsVim`, pa ponovi korake iz načina D |

Ako je psvim učitan u otvorenom PowerShell prozoru, nova verzija važi tek u novom prozoru. Za MSI je najbolje da pre nadogradnje zatvoriš PowerShell prozore. U suprotnom Windows Installer može tražiti restart da zameni zaključan DLL.

Trenutna verzija: `(Get-Module -ListAvailable PsVim).Version`

---

## 10. Uklanjanje

| Instalirano preko | Uklanjanje |
|---|---|
| MSI | *Settings → Apps → Installed apps → psvim → Uninstall*, pa `Set-PsVimProfile -Remove` ako si ga koristio |
| ZIP | `Uninstall.cmd` iz raspakovanog foldera |
| jedna komanda | `& ([scriptblock]::Create((irm https://raw.githubusercontent.com/vladimir-stojadinov/psvim/main/setup.ps1))) -Uninstall` |
| ručno | obriši folder `PsVim` iz `Modules`, pa `Set-PsVimProfile -Remove` ako si ga koristio |

`Set-PsVimProfile -Remove` treba pokrenuti **pre** brisanja modula, jer posle brisanja ta komanda više ne postoji. Ako je kasno, obriši ručno blok `# >>> psvim >>>` … `# <<< psvim <<<` iz profila (`notepad $PROFILE.CurrentUserAllHosts`).

Konfiguracija (`%USERPROFILE%\.config\psvim`) i lista nedavnih fajlova (`%LOCALAPPDATA%\psvim`) ostaju. Obriši ih ručno ako ti ne trebaju.

---

## 11. Rešavanje problema

### 11.1 „running scripts is disabled on this system“

Windows PowerShell 5.1 podrazumevano ne dozvoljava skripte, a modul je PowerShell skripta. Dozvoli lokalne skripte samo za svog korisnika:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

Ako `Get-ExecutionPolicy -List` pokazuje `AllSigned` ili vrednost postavljenu kroz `MachinePolicy`/`UserPolicy` (grupna politika firme), ovo ne možeš sam da promeniš. Obrati se IT-u.

### 11.2 „psvim : The term 'psvim' is not recognized…“

- Otvori **novi** PowerShell prozor posle instalacije.
- Proveri da li je modul tamo gde ga PowerShell traži:

  ```powershell
  Get-Module -ListAvailable PsVim
  $env:PSModulePath -split ';'
  ```

  Folder iz kog je modul (`…\Modules`) mora biti na toj listi.
- Ako si instalirao samo za jedan PowerShell (npr. ručno za 5.1), u drugom ga nema. Pokreni instalaciju i tamo.
- Probaj eksplicitno `Import-Module PsVim`. Poruka greške obično kaže tačan uzrok.

### 11.3 Antivirus blokira instalaciju

- `irm … | iex` blokiraju mnogi antivirusi (Cynet, Defender ASR pravila…). Koristi MSI ili ZIP.
- Ako je blokiran i MSI, IT može da doda izuzetak za `psvim-0.2.0.msi` (proveri SHA256 iz `SHA256SUMS.txt`), ili koristi način D.

### 11.4 „Windows protected your PC“ (SmartScreen)

Javlja se jer instaler nije digitalno potpisan. Klikni *More info → Run anyway*.

### 11.5 „Could not load file or assembly … PsVim.Core.dll“ ili „Operation is not supported“

Windows PowerShell 5.1 odbija DLL koji je obeležen kao preuzet sa interneta. Odblokiraj ga (način D, korak 3) ili instaliraj preko MSI-ja, jer MSI ne ostavlja tu oznaku. Proveri i .NET Framework (poglavlje 1): potreban je 4.7.2 ili noviji.

### 11.6 Setup kaže da ne može da obriše modul

Modul je učitan u nekom otvorenom PowerShell prozoru. Setup u tom slučaju sklanja stare fajlove i instalira novu verziju pored njih, a nova važi od sledećeg prozora. Ako i to ne uspe, zatvori sve PowerShell prozore (i VS Code, ako koristi PowerShell ekstenziju) i pokreni ponovo.

### 11.7 Čudni znakovi, kvadratići ili pomereni redovi

- Koristi **Windows Terminal** umesto starog konzolnog prozora.
- Kvadratići umesto ikonica znače da font nema te znakove. Instaliraj Nerd Font ili isključi ikonice: `:set nonerdfont`.
- Ekran se poremetio: `Esc` ili `:redraw` ga ponovo iscrtava.

### 11.8 Vim mod u promptu ne radi ili smeta

- Proveri da `$PROFILE` sadrži `Enable-PsVimPrompt`: `Get-Content $PROFILE.CurrentUserAllHosts`.
- Isključi ga za trenutni prozor sa `Disable-PsVimPrompt`, a trajno sa `Set-PsVimProfile -NoPrompt`.

---

Nešto nije pokriveno? Otvori issue na https://github.com/vladimir-stojadinov/psvim/issues
