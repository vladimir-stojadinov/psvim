# psvim — kratko uputstvo

## 1. Instalacija (jednom)

Potrebno: PowerShell 7.4+ (najbolje u Windows Terminal-u).

```powershell
git clone https://github.com/vladimir-stojadinov/psvim
cd psvim
./build.ps1 -Install        # treba .NET 8 SDK; bez njega: Import-Module .\module\PsVim
```

U `$PROFILE` dodaj (otvori ga sa `psvim $PROFILE`):

```powershell
Import-Module PsVim
Enable-PsVimPrompt          # vim mod u promptu (opciono)
```

## 2. Pokretanje

```powershell
psvim fajl.ps1        # otvori fajl
psvim +42 fajl.ps1    # otvori na liniji 42
psvim .               # stablo fajlova
psvim                 # početni ekran
Get-Process | psvim   # izlaz komande u editoru
```

Pokreći iz foldera projekta — pretraga fajlova i teksta radi nad trenutnim folderom.

## 3. Prvih deset minuta

| Želim da… | Kucam |
|---|---|
| počnem da pišem | `i` (ili `a`, `o`), pa `Esc` za povratak |
| sačuvam / izađem | `Ctrl+s` · `:w` · `:q` · `:wq` · `:q!` (bez čuvanja) |
| poništim / vratim | `u` · `Ctrl+r` |
| pronađem fajl | `razmak razmak` |
| pretražim tekst u projektu | `razmak /` |
| otvorim stablo fajlova | `razmak e` (fokus: `Ctrl+h` / `Ctrl+l`) |
| pređem na drugi otvoren fajl | `H` / `L` ili `razmak ,` |
| skočim bilo gde na ekranu | `s` + dva slova + oznaka |
| vidim sve prečice | pritisnem `razmak` i sačekam, ili `razmak s k` |
| pročitam pomoć | `:help` |

**Razmak je „leader“.** Pritisni ga i sačekaj — iskače meni sa grupama (`f` fajlovi, `s` pretraga, `b` baferi, `c` kod, `g` git, `u` izgled, `q` izlaz).

## 4. Osnove vima u jednom pasusu

Tasteri se kombinuju kao rečenica: **operator + kretanje**. `d` briše, `c` menja, `y` kopira; `w` je reč, `$` kraj linije, `}` pasus. Dakle `dw` briše reč, `c$` menja do kraja linije, `yy` kopira liniju, `p` lepi. Ispred može broj: `3dd` briše tri linije. Tekst objekti rade „iznutra“: `ciw` promeni reč, `di"` obriši sadržaj navodnika, `ci{` promeni telo bloka. `.` ponavlja poslednju izmenu.

## 5. PowerShell pomoć u editoru

- Greške u kodu se vide odmah (crveno `E` levo i poruka na kraju linije); `]d` / `[d` skaču kroz njih.
- `K` na komandi → `Get-Help`.
- U insert modu `Ctrl+Space` → dopuna (komande, parametri, promenljive, putanje).
- `gd` → definicija funkcije, `razmak c x` → pokreni fajl, `razmak c f` → formatiraj (uz PSScriptAnalyzer).

## 6. Prompt (posle `Enable-PsVimPrompt`)

`Esc` prebacuje u normal mod. Onda: `razmak` → meni, `razmak razmak` otvori fajl, `razmak /` grep, `razmak :` istorija, `v` uredi komandu u psvim-u, `K` Get-Help. U insert modu: `Ctrl+t` ubaci putanju fajla, `Ctrl+r` istorija komandi.

## 7. Podešavanja

`razmak f c` otvara `~/.config/psvim/config.json`; snimanjem se odmah primenjuje. Najčešće:

```json
{ "theme": "catppuccin", "relativeNumber": false, "tabStop": 2, "shiftWidth": 2, "nerdFont": true }
```

Privremeno: `:set nornu`, `:colorscheme gruvbox`.

## 8. Kad nešto ne radi

- `:checkhealth` — pokazuje šta je dostupno (clipboard, Get-Help, PSScriptAnalyzer, git, lazygit).
- Ekran se čudno iscrtao → `Esc` ili `:redraw`.
- Brojevi linija se „menjaju“ → to su relativni brojevi; isključi sa `razmak u L`.
- `build.ps1 -Install` javlja da ne može da obriše modul → zatvori PowerShell prozore u kojima je psvim učitan.
