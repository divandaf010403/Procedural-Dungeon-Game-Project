# 🚨 PENTING - Cara Lihat Kota Setelah Generate

Jika Anda **tidak melihat kota sama sekali** setelah klik "Generate City", ikuti langkah ini:

## ⚡ Quick Fix: Frame Camera

1. **Klik kanan** di component `CityGenerator` (Inspector)
2. Pilih **Frame Camera**
3. Unity akan otomatis memposisikan Main Camera untuk melihat seluruh kota

## ✅ Yang Otomatis Terjadi Saat Generate

Saat Anda klik **Generate City**, sistem otomatis akan:
- ✅ Generate ground plane hijau besar (sebagai referensi visual)
- ✅ Setup Main Camera untuk melihat kota dari sudut pandang atas
- ✅ Tambah Directional Light (matahari) kalau belum ada
- ✅ Generate kota dengan semua advanced features

## 🎮 Langkah Setup Pertama Kali

1. **Buka Unity**, tunggu compile selesai
2. **Buat GameObject kosong**: `GameObject > Create Empty`
3. **Rename** jadi `CityGenerator`
4. **Add Component** → cari `City Generator` (script)
5. **Klik kanan header script** → **Generate City**
6. **Otomatis**: camera di-frame untuk melihat kota 🎉

Jika masih tidak terlihat, klik kanan lagi → **Frame Camera**

## 🐛 Troubleshooting

### "Kota tidak terlihat setelah generate"
→ Klik kanan header script → **Frame Camera**

### "Semua bangunan hitam/gelap"
→ Klik kanan header script → **Frame Camera** (akan tambah sunlight)

### "Saya pakai Render Pipeline lain (Bukan URP)"
→ Script sekarang punya fallback: URP → Standard → Diffuse → Sprites/Default

### "Kota terlalu kecil/besar"
→ Adjust `City Size` di Inspector (50-1000)

### "Mau lihat tanpa harus regenerate"
→ Camera di Scene view bisa di-zoom manual dengan scroll wheel

## 📋 Parameter Inspector

| Parameter | Default | Penjelasan |
|-----------|---------|------------|
| **City Size** | 300 | Ukuran total kota |
| **Block Size** | 40 | Ukuran blok (antar jalan) |
| **Road Width** | 6 | Lebar jalan |
| **Random Seed** | 42 | Ubah untuk kota berbeda |
| **Generate On Start** | true | Auto-generate saat Play |
| **Add Environment Details** | true | Lampu, mobil, traffic light |
| **Generate River** | true | Sungai + jembatan |
| **Building Density** | 0.7 | Kepadatan bangunan |
| **River Width** | 18 | Lebar sungai |

## 🎯 Context Menu yang Tersedia

Klik kanan pada header `City Generator` di Inspector:
- **Generate City** - Buat kota baru
- **Clear City** - Hapus semua
- **Frame Camera** - Posisikan camera untuk lihat kota

## 🔮 Advanced Tips

- Coba ubah `Random Seed` dari 1-999 untuk kota berbeda
- Tambah custom Building Prefab ke field `Building Prefabs`
- Disable `Generate River` kalau sungai menutupi area penting
- Pakai `Building Density = 0.5` untuk kota lebih sparse