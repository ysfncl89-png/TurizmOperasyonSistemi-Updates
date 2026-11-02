# TOS Lite Velopack Güncelleme Entegrasyonu

## Strategy
MutfaGO projesindeki yaklaşımı referans alarak TOS Lite güncellemelerini yalnızca Velopack ve GitHub Releases üzerinden yürüt; INNO Setup akışını kaldır.

## Preferences
- **Flow Mode**: Automatic
- **Commit Strategy**: Manual
- **Pace**: Methodical
- **Scope**: TurizmOperasyonSistemi.slnx
- **Package**: Velopack, latest stable
- **Compatibility**: INNO Setup güncelleme yolu kaldırılacak; GitHub Releases + Velopack tek güncelleme yolu olacak
- **Reference Project**: C:\Users\turiz\Desktop\MutfaGO yalnızca inceleme ve davranış referansı olarak kullanılacak
- **CI Security**: GitHub Actions secrets/vars only; no hardcoded tokens
- **Source Control Constraint**: Git executable unavailable; remain on current `master` without branch changes
- **Repository Rules**: Preserve PersonsWizardView.xaml behavior; obey .github/copilot-instructions.md including PersonsGeneralView violet ComboBox and LocalAppData manifest schema

## Decisions
- User changed the strategy: INNO Setup will no longer be maintained for updates.
- MutfaGO's update implementation will be inspected first and its applicable patterns adapted to TOS Lite.
- Velopack packaging and GitHub Releases publishing will be the sole distribution/update path.
- GitHub Actions will use repository secrets/variables for release credentials and repository metadata.
- Use an opt-in configuration flag until the new flow is validated; avoid hardcoded credentials.

## Custom Instructions
<!-- Task-specific overrides: "For {taskId}: {instruction}" -->
