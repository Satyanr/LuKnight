# Lu-Knight User Guide

Lu-Knight adalah desktop companion Windows yang menggabungkan karakter
interaktif, local-first assistant, voice, dan Windows automation yang dibatasi
oleh permission dan confirmation.

## First run

Instalasi baru dimulai dengan capability sensitif berikut dalam keadaan OFF:

- Application awareness
- File context
- Clipboard context
- System context
- Screen context
- Voice input
- Desktop actions
- Proactive companion

Menyelesaikan halaman Panduan awal tidak mengaktifkan capability tersebut.

Setiap capability dapat diaktifkan secara terpisah melalui:

Settings → AI & Chat

## Local mode

Lu-Knight tetap dapat berjalan tanpa Gemini API key.

Local mode tetap menyediakan fitur yang dapat diselesaikan secara lokal,
termasuk supported deterministic desktop routing.

Gemini tidak menjadi Windows executor.

## Gemini

Gemini digunakan untuk percakapan dan reasoning yang memang membutuhkan AI.

Jika Gemini aktif, context yang secara eksplisit diizinkan dan digunakan untuk
request dapat dikirim ke Gemini.

File, clipboard, system, dan screen context bersifat request-scoped dan tidak
otomatis menjadi history untuk request berikutnya.

## Application awareness

Application awareness menggunakan metadata aplikasi terbatas.

Pada assistant/provider context, Lu-Knight menggunakan informasi aplikasi yang
dibatasi seperti process identity dan kategori aplikasi.

Application awareness tidak berarti membaca seluruh isi layar atau dokumen.

Mematikan Application awareness juga mematikan Proactive companion.

Menyalakannya kembali tidak otomatis menyalakan Proactive companion.

## File context

File context:

- harus diaktifkan terlebih dahulu
- hanya membaca file yang diminta secara eksplisit
- read-only
- memiliki batas ukuran
- memblokir kategori file credential/secret yang dikenal
- tidak memberikan izin untuk menjalankan instruction yang terdapat di file

## Clipboard context

Clipboard:

- hanya dibaca ketika diminta
- tidak dimonitor di background
- tidak otomatis dimasukkan ke request berikutnya

## System context

System context dapat menyediakan informasi terbatas seperti:

- versi Windows
- CPU architecture
- logical processor count
- RAM
- battery/power state
- network availability
- uptime

Informasi seperti username, computer name, IP, MAC address, Wi-Fi SSID,
hardware serial number, dan location tidak dimasukkan ke normal system context.

## Screen context

Screen context adalah one-shot capture ketika diminta secara eksplisit.

Screen capture bukan continuous monitoring.

## Voice input

Voice input menggunakan Push-to-Talk.

Audio:

- hanya direkam ketika Listening aktif
- diproses melalui local Speech-to-Text
- tidak disimpan sebagai audio file oleh normal voice flow
- tidak dikirim ke Gemini sebagai audio

Hanya hasil transkripsi teks yang dapat diteruskan ke Assistant.

## Desktop actions

Mengaktifkan Desktop actions tidak memberikan izin permanen untuk melakukan
semua tindakan.

Permission level:

### Observe only

Tidak mengizinkan desktop mutation.

### Navigation

Mengizinkan supported navigation seperti membuka/fokus aplikasi dan supported
Explorer navigation/search.

Confirmation tetap diperlukan.

### Interaction

Navigation ditambah supported safe UI interaction dan safe text input.

### Sensitive

Interaction ditambah supported sensitive actions.

Sensitive action menggunakan two-stage confirmation.

## Desktop action safety

Lu-Knight:

- lebih memilih Windows UI Automation daripada coordinate clicks
- melakukan target revalidation sebelum execution
- tidak mengetik ke password/protected credential fields
- tidak menjalankan arbitrary PowerShell/CMD/scripts
- tidak mengeksekusi arbitrary EXE path dari AI
- tidak membiarkan Gemini mengeksekusi Windows action secara langsung
- tidak menganggap generic OK/Yes sebagai safe arbitrary confirmation target

Mouse/keyboard fallback hanya digunakan pada target yang sudah melalui validation
dan tidak menurunkan risk classification.

## Skills and workflows

Skills dan workflows bukan trusted executors.

Mereka menghasilkan langkah yang tetap melewati:

- normal routing
- permission checks
- confirmation
- target preparation
- native revalidation

Setiap langkah multi-step plan dikonfirmasi dan disiapkan secara independen.

## Scheduler and reminders

Scheduled time bukan permission untuk menjalankan action.

Alurnya:

Reminder
→ user membuka card
→ Run
→ normal workflow routing
→ normal confirmation
→ execution

Reminder presentation tidak berarti workflow sudah completed.

## Proactive companion

Proactive companion:

- opt-in
- membutuhkan Application awareness
- menggunakan kategori aplikasi umum
- rate-limited
- tidak otomatis melakukan desktop action

Suggestion hanya menawarkan bantuan.

Memilih suggestion tidak memberikan permission untuk Windows action.

## Settings

Settings menggunakan autosave.

Footer menampilkan status persistence saat ini.

Jika penyimpanan disk gagal, perubahan dapat tetap berlaku untuk sesi sekarang
tetapi aplikasi akan memberi tahu bahwa persistence gagal.

## Privacy summary

Lu-Knight menggunakan prinsip:

Local task → local processing

jika task dapat diselesaikan secara deterministik dan aman.

Gemini dapat memahami atau merencanakan.

Local validated action engine melakukan Windows execution.

## System tray

Tray menyediakan akses seperti:

- Show / Hide Lu-Knight
- Chat
- Settings
- reminder/suggestion entry
- Exit

Jika system tray gagal dibuat, Lu-Knight tetap dibuat reachable melalui taskbar.

## Testing status

Automated/native harness success tidak sama dengan physical Windows PASS.

Physical microphone, speaker/TTS, multi-monitor, tray-shell, installer, dan
updater end-to-end memiliki acceptance evidence terpisah.

Lihat:

- REGRESSION_MATRIX.md
- REGRESSION_SIGNOFF.md
