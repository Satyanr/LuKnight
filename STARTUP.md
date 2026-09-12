# 6C — Start with Windows

Buka menu tray → Settings → General, lalu aktifkan **Start Lu-Knight with Windows**. Pilihan ini langsung tersimpan untuk akun Windows saat ini, tanpa administrator. Default pada akun yang belum mengaktifkannya adalah nonaktif.

**Start hidden in System Tray** menyimpan pilihan untuk menyembunyikan karakter saat login. Tooltip menjelaskan bahwa pembukaan manual tetap menampilkan karakter. Saat mulai tersembunyi, gunakan menu tray untuk menampilkan karakter atau membuka Settings. Jika tray gagal dibuat, karakter tetap ditampilkan. Restart dari menu tray juga menampilkan karakter.

Sejak fase 6F, pilihan StartHidden dimigrasikan ke `settings.json`; entry startup tetap di HKCU Run. Detail terkini ada di [PHASE6_RELEASE.md](PHASE6_RELEASE.md).

## Penyimpanan dan lifecycle awal 6C

- `Services/StartupService.cs` menyediakan `IsEnabled`, `Enable`, `Disable`, dan `Validate`.
- Startup memakai nilai string `LuKnight` di `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, dengan command `"lokasi\LuKnight.exe" --startup`. Menonaktifkannya hanya menghapus nilai milik Lu-Knight.
- Pilihan tersembunyi memakai DWORD `StartHidden` di `HKCU\Software\LuKnight`. Ini tetap disimpan saat startup dinonaktifkan. Penyimpanan konfigurasi aplikasi lainnya tetap masuk fase 6F.
- Membuka Settings hanya membaca status. Perubahan checkbox langsung disimpan dan dibaca kembali; kegagalan izin menampilkan pesan dan mengembalikan checkbox ke nilai sebenarnya.
- Saat aplikasi dijalankan dari lokasi baru, `Validate` memperbarui command yang sudah terdaftar. Validasi tidak mengaktifkan startup yang sebelumnya nonaktif. Pengguna harus menjalankan aplikasi dari lokasi baru terlebih dahulu agar path lama dapat diperbaiki.
- Command mengutip path yang mengandung spasi, mendukung host `dotnet` dengan path assembly, memeriksa keberadaan file, dan menolak command lebih dari 260 karakter sesuai [dokumentasi Microsoft Run keys](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys).
- Windows dapat menonaktifkan startup melalui Settings/Task Manager. Checkbox aplikasi menunjukkan registrasi Run milik Lu-Knight; aplikasi tidak mengubah kebijakan atau override Windows tersebut.

## Verifikasi

```powershell
dotnet build LuKnight.csproj -c Release --no-restore
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release -- --startup
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release -- --settings
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release
```

468 pemeriksaan lolos, termasuk 28 pemeriksaan startup dan 31 pemeriksaan Settings. Pengujian startup memakai penyimpanan tiruan, sehingga tidak mengubah registry akun pengguna. Cakupan meliputi default nonaktif, persistensi pilihan, perbaikan path, kegagalan izin, perubahan eksternal, command tidak valid, serta keputusan tampil/tersembunyi berdasarkan argumen dan ketersediaan tray. Render Settings diperiksa secara offscreen. Login Windows, registry nyata, dan klik tray native belum diuji secara interaktif.
