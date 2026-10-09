# Lu-Knight Settings

Settings adalah konfigurasi utama Lu-Knight dan menggunakan autosave.

Settings dapat dibuka dari tray meskipun mascot sedang tersembunyi. Hanya satu
instance Settings digunakan; membuka Settings lagi memfokuskan window yang sama.

## Panduan awal

Fresh installation membuka halaman Panduan awal.

Panduan ini hanya menjelaskan capability dan tidak memberikan permission.

Sensitive capabilities tetap OFF sampai diaktifkan secara terpisah.

Existing installations yang bermigrasi dari settings schema lama tidak dipaksa
mengulang onboarding.

## General

Menyediakan:

- Always on top
- Show / Hide Lu-Knight
- Reset character position
- runtime renderer/status
- Start with Windows
- Start hidden in System Tray

Window placement dan mascot placement dipulihkan dengan monitor reachability
validation.

## Behavior

Mengatur autonomous character behavior:

- activity
- movement speed
- sleep
- window exploration
- jump/window behavior
- cursor interaction

Behavior setting tidak memberikan permission kepada Assistant untuk melakukan
desktop action.

## AI & Chat

Menyediakan:

- provider/model
- Gemini API key
- conversation behavior
- long-term memory
- Application awareness
- Proactive companion
- File context
- Clipboard context
- System context
- Screen context
- Voice input
- Text-to-Speech
- Desktop actions
- Desktop permission level

Bagian Access summary menampilkan state efektif capability utama.

Jika Application awareness dimatikan, Proactive companion ikut dimatikan dan
tidak hidup kembali otomatis ketika Application awareness dinyalakan.

## Credentials

Gemini API key disimpan di Windows Credential Manager.

Password field Settings tidak menampilkan kembali credential yang sudah
tersimpan.

Environment variable GEMINI_API_KEY dapat digunakan sebagai development
fallback.

## Desktop permission

Available levels:

- Observe only
- Navigation
- Interaction
- Sensitive

Desktop actions tetap membutuhkan proposal, validation, confirmation, dan fresh
target revalidation.

Sensitive actions menggunakan two-stage confirmation.

## Voice

Voice input adalah Push-to-Talk dan local Speech-to-Text.

Text-to-Speech menggunakan Windows speech voices.

Voice input dan spoken replies adalah pengaturan terpisah.

## About

About menampilkan:

- version
- build
- repository
- update controls

Update tidak dipasang otomatis. Install & Restart harus dipilih secara eksplisit.

## Persistence

Perubahan Settings disimpan otomatis.

Settings persistence menggunakan committed primary/backup recovery.

Corrupt primary dapat dipulihkan dari validated committed backup.

Unsupported future schema dibuka read-only dan tidak ditimpa oleh versi aplikasi
yang lebih lama.

## Accessibility

Settings mendukung normal keyboard Tab navigation dan focus indication.

Navigation sidebar, credential input, privacy controls, dan Desktop action
controls memiliki accessibility names sehingga dapat diidentifikasi melalui
Windows accessibility/UI Automation.

## Verification

```powershell
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release -- --first-run
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release -- --settings-ux
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release -- --ux-acceptance
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release -- --product
```

Offscreen/synthetic UI testing does not establish physical tray, monitor,
microphone, or speaker PASS.
