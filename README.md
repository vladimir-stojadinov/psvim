# psvim — vim i LazyVim za PowerShell

Modalni editor koji se pokreće direktno iz PowerShell-a (`psvim fajl.ps1`) i vim mod u samom promptu, sa onim što LazyVim čini prijatnim: leader meni sa which-key popup-om, fuzzy pretraga fajlova i teksta sa pregledom, stablo fajlova, flash skokovi, statusna linija, lista bafera, dijagnostika i dopuna koda.

![editor](docs/1-editor.png)

## Šta radi

**Editor (`psvim`)**
- Modovi: Normal, Insert, Visual, V-Line, Replace, komandna linija
- Kretanja: `h j k l w b e ge W B E 0 ^ $ g_ gg G f F t T ; , % { } M`, brojevi (`3w`, `5j`), `/ ? n N * #`, oznake `m` `'` `` ` ``, skokovi `<C-o> <C-i>`
- Operatori: `d c y > < = gu gU g~ gc` uz bilo koje kretanje ili tekst objekat
- Tekst objekti: `iw aw iW aW i" a" i' a' i( a( i{ a{ i[ a[ i< ip ap` plus `if af` (funkcija) i `ig` (ceo fajl)
- `.` ponavljanje, makroi (`qa … q`, `@a`, `@@`), registri (`"a`, `"A`, `"_`, `"0`, `"+`), `u` / `<C-r>`, `<C-a>` / `<C-x>`
- Ex komande: `:w :q :wq :x :e :bn :bd :ls :%s/a/b/g :g/x/d :v/x/d :sort :norm :m :t :retab :!cmd :r !cmd :'<,'>!Sort-Object`
- Sistemski clipboard je podrazumevan (kao `clipboard=unnamedplus` u LazyVim-u)

**LazyVim sloj**
- Leader je razmak; posle kratke pauze iskače which-key meni sa grupama
- Fuzzy picker (Telescope / snacks): fajlovi, live grep, baferi, nedavni fajlovi, simboli, linije bafera, prečice, istorija komandi, dijagnostika, git status, teme
- Stablo fajlova (neo-tree): dodaj, obriši, preimenuj, kopiraj, skriveni fajlovi
- Flash (`s`): otkucaj 1–2 slova pa oznaku za skok; radi i uz operatore (`ds…`)
- Statusna linija (lualine), linija bafera (bufferline), plutajuća komandna linija (noice), obaveštenja
- Auto zagrade (mini.pairs), komentarisanje `gcc` / `gc` (mini.comment), vodiči uvlačenja, `<Esc>` gasi isticanje pretrage
- Teme: tokyonight (podrazumevana), catppuccin, gruvbox, kanagawa
- Dashboard pri pokretanju bez fajla

**PowerShell integracija**
- Bojenje sintakse za PowerShell (here-string, `$promenljive` u stringovima, tipovi, parametri, operatori); osnovno bojenje i za C#, JSON, Markdown, XML, YAML, Python, JS/TS, Go, Rust, SQL, Lua, shell
- Dijagnostika uživo preko PowerShell parsera: greške i nepoznate komande u margini i kao virtuelni tekst (`]d` `[d`, `␣xx`)
- PSScriptAnalyzer (ako je instaliran): upozorenja pri snimanju i formatiranje (`␣cf`)
- `K` prikazuje `Get-Help` za komandu pod kursorom
- Dopuna u insert modu (`<C-Space>` / `<C-n>`) preko `TabExpansion2`: komande, parametri, promenljive, putanje, članovi
- `gd` nalazi definiciju funkcije (u fajlu ili projektu), `gr` reference, `gf` otvara putanju pod kursorom (razume `$PSScriptRoot`)
- `␣cx` pokreće trenutni fajl i prikazuje izlaz, `<C-/>` otvara ugnežđeni terminal, `␣gg` lazygit
- `Get-Process | psvim` otvara izlaz komande u baferu

**Prompt (PSReadLine)**
- Vi mod sa oblikom kursora (blok / crta), tokyonight boje
- U normal modu razmak otvara leader meni: `␣␣` otvori fajl, `␣fi` ubaci putanju, `␣/` grep, `␣:` istorija, `␣fd` promeni folder, `␣v` ili `v` uredi komandu u psvim-u, `K` Get-Help
- U insert modu: `Ctrl+t` ubaci putanju, `Ctrl+r` istorija (fzf stil), `Tab` meni dopune, `Ctrl+y` prihvati predlog

![which-key](docs/2-whichkey.png)
![picker](docs/3-picker.png)
![stablo](docs/4-tree.png)

Kratko uputstvo za početak: [UPUTSTVO.md](UPUTSTVO.md)

## Instalacija

Potrebno: **PowerShell 7.4+** (Windows Terminal preporučen). Za build još treba .NET 8 SDK.

Gotov `PsVim.Core.dll` već je u `module/PsVim/bin`, pa build nije obavezan:

```powershell
git clone https://github.com/vladimir-stojadinov/psvim
Import-Module .\psvim\module\PsVim
psvim
```

Build iz izvornog koda i instalacija za trenutnog korisnika:

```powershell
cd psvim
./build.ps1 -Test      # build + 234 testa
./build.ps1 -Install   # kopira modul u Documents\PowerShell\Modules\PsVim
```

U `$PROFILE` (otvori sa `psvim $PROFILE`):

```powershell
Import-Module PsVim
Enable-PsVimPrompt          # vim mod u promptu; izostavi ako ti ne treba
```

Preporuke:
- Nerd Font (npr. `JetBrainsMono Nerd Font`) i `:set nerdfont` za ikonice
- `Install-Module PSScriptAnalyzer` za lint i formatiranje
- `winget install JesseDuffield.lazygit` za `␣gg`

## Korišćenje

```powershell
psvim skripta.ps1          # otvori fajl
psvim +42 skripta.ps1      # otvori na liniji 42
psvim .                    # stablo fajlova
psvim                      # dashboard
Get-ChildItem | psvim      # izlaz komande u baferu
Edit-PsVimConfig           # konfiguracija
```

Alias `vim` / `vi` postoji samo ako pravi vim nije instaliran. `v` je uvek alias za `psvim`.

U editoru: `:help` (ili `F1`) otvara pregled svih prečica, `␣sk` ih pretražuje, a `:checkhealth` pokazuje šta je dostupno.

### Najvažnije leader prečice

| Prečica | Radnja | Prečica | Radnja |
|---|---|---|---|
| `␣␣` `␣ff` | pronađi fajl | `␣/` `␣sg` | grep |
| `␣,` `␣fb` | baferi | `␣fr` | nedavni fajlovi |
| `␣e` | stablo fajlova | `␣fn` | novi fajl |
| `␣sw` | traži reč pod kursorom | `␣ss` | simboli (funkcije) |
| `␣sb` | linije bafera | `␣sk` | sve prečice |
| `␣bd` | zatvori bafer | `␣bo` | zatvori ostale |
| `␣cf` | formatiraj | `␣cx` | pokreni fajl |
| `␣cd` | dijagnostika linije | `␣xx` | lista dijagnostike |
| `␣cR` | preimenuj fajl | `␣sr` | traži i zameni reč |
| `␣gg` | lazygit | `␣gs` `␣gl` `␣gd` `␣gb` | git status / log / diff / blame |
| `␣uC` | tema | `␣ul` `␣uL` `␣ud` `␣up` | brojevi / relativni / dijagnostika / auto zagrade |
| `␣fc` | konfiguracija | `␣qq` | izlaz |

Ostalo: `H` / `L` prethodni/sledeći bafer, `<C-h>` / `<C-l>` fokus stablo/editor, `<C-s>` sačuvaj, `s` flash, `gcc` komentar.

## Konfiguracija

`~/.config/psvim/config.json` (ili `$env:PSVIM_CONFIG`). Snimanje ovog fajla u psvim-u odmah ga ponovo učitava.

```jsonc
{
  "theme": "tokyonight",        // catppuccin, gruvbox, kanagawa
  "relativeNumber": true,
  "tabStop": 4,
  "shiftWidth": 4,
  "expandTab": true,
  "nerdFont": false,
  "autoPairs": true,
  "clipboard": "unnamedplus",   // "" = samo interni registri
  "autoFormatOnSave": false,
  "whichKeyDelay": 300,
  "ignore": [".git", "node_modules", "bin", "obj"],
  "keymaps": [
    { "mode": "n", "lhs": "<leader>td", "rhs": ":!Get-Date<CR>", "desc": "Datum" },
    { "mode": "n", "lhs": "<C-t>", "rhs": ":Explore<CR>" }
  ]
}
```

Opcije se menjaju i u letu: `:set rnu! ts=2 sw=2`, `:colorscheme catppuccin`.

`$PsVimOptions.CheckCommands = $false` isključuje upozorenja za nepoznate komande.

## Arhitektura

```
src/PsVim.Core/        C# (.NET 8), bez spoljnih zavisnosti
  Editor*.cs           modovi, kretanja, operatori, ex komande, LazyVim sloj
  Buffer.cs            tekst, undo/redo, učitavanje/snimanje (čuva BOM i CRLF)
  Screen.cs            dvostruki bafer, šalje terminalu samo razlike (truecolor ANSI)
  Syntax.cs            lekseri (PowerShell + opšti)
  Ui.cs                picker, fuzzy algoritam, which-key
  Files.cs             nabrajanje fajlova, grep, nedavni fajlovi, stablo
  PromptUi.cs          picker-i i which-key za PSReadLine
module/PsVim/          PowerShell modul: kuke (clipboard, Get-Help, parser,
                       PSScriptAnalyzer, TabExpansion2) i PSReadLine prečice
tests/PsVim.Tests/     234 testa koji "kucaju" tastere i proveravaju rezultat
```

Editor ne zavisi od PowerShell SDK-a; sve što traži PowerShell ide kroz `PsVim.Hooks` delegate koje modul popunjava skriptblokovima. Zato se jezgro testira bez PowerShell-a, a modul radi na Windows-u, Linux-u i macOS-u.

## Ograničenja (za sada)

- Nema podele prozora (split) ni prelamanja dugih linija (koristi se horizontalno skrolovanje)
- Široki znakovi (emoji, CJK) prikazuju se kao `□`, a nevidljivi i kontrolni kao `?` (da se ekran ne bi raspao)
- Binarni fajlovi (exe, dll, zip) otvaraju se samo za čitanje
- Dijagnostika i dopuna su PowerShell-specifične; drugi jezici imaju bojenje i dopunu rečima iz bafera
- Undo istorija se ne čuva između sesija

Ideje za sledeće verzije: git znakovi u margini (gitsigns), split prozori, `:terminal` u prozoru, surround (`ys`, `cs`, `ds`), višestruki kursori.
