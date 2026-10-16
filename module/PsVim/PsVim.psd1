@{
    RootModule           = 'PsVim.psm1'
    ModuleVersion        = '0.1.0'
    GUID                 = '6d0b1c2e-3a41-4f7e-9a51-6c2f2b7f9e10'
    Author               = 'Vladimir Stojadinov'
    Description          = 'vim/LazyVim za PowerShell: modalni editor (psvim) i vim mod u promptu sa which-key, fuzzy finder-om, stablom fajlova i dijagnostikom.'
    PowerShellVersion    = '7.4'
    CompatiblePSEditions = @('Core')
    FunctionsToExport    = @('Invoke-PsVim', 'Edit-PsVimConfig', 'New-PsVimHooks', 'Enable-PsVimPrompt', 'Disable-PsVimPrompt', 'Edit-PsVimPromptLine', 'Show-PsVimHelpForWord', 'Get-PsVimPromptKeys')
    AliasesToExport      = @('psvim', 'v', 'vim', 'vi')
    CmdletsToExport      = @()
    VariablesToExport    = @()
    PrivateData          = @{
        PSData = @{
            Tags = @('vim', 'editor', 'lazyvim', 'psreadline', 'tui')
        }
    }
}
