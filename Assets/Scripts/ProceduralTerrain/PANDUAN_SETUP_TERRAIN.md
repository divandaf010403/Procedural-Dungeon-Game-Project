# 🌍 Panduan Setup Terrain

Terdapat **2 mode** terrain generation:

| Mode | Cocok Untuk | Ukuran Dunia |
|------|-------------|-------------|
| **TerrainPipeline** | Map terbatas, testing | 1000-4000m |
| **InfiniteTerrainManager** | Open world, Minecraft-style | ♾️ Tak terbatas |

---

## Mode 1: Terrain Pipeline (Map Terbatas)

### Setup:
1. Hierarchy → **3D Object → Terrain**
2. Atur Terrain Settings: Width/Length = `1000-4000`, **Height = `300-600`**
3. **Add Component → Terrain Pipeline**
4. Klik **🎲 Generate New Random World**

---

## Mode 2: Infinite Terrain Manager (Dunia Tak Terbatas) 🌟

### Langkah 1: Buat Manager Object
1. Hierarchy → klik kanan → **Create Empty**
2. Rename menjadi **"WorldManager"** atau nama lain
3. **Add Component → Infinite Terrain Manager**

### Langkah 2: Buat Player / Camera
1. Jika belum punya player, buat sementara:
   - Hierarchy → **3D Object → Capsule** (sebagai player)
   - Posisikan di **(0, 150, 0)** ← harus di atas terrain!
2. Drag player/camera ke slot **"Player"** di Inspector InfiniteTerrainManager

### Langkah 3: Hapus Terrain Lama (Jika Ada)
- Jika ada Terrain object di Hierarchy dari setup sebelumnya, **HAPUS**
- InfiniteTerrainManager akan membuat Terrain sendiri

### Langkah 4: Atur Parameter
| Parameter | Default | Penjelasan |
|-----------|---------|------------|
| **Chunk Size** | `256` | Ukuran tiap chunk (meter). Lebih besar = lebih lambat generate |
| **View Distance** | `4` | Berapa chunk terlihat di setiap arah. 4 = grid 9×9 |
| **Terrain Height** | `400` | Tinggi maksimum. **Minimal 300!** |
| **Heightmap Res** | `129` | Detail per chunk. 129 = ringan, 257 = detail |

### Langkah 5: Generate!
- Klik **🎲 Generate New Random World** di Inspector
- Atau tekan **Play** — chunk akan otomatis di-generate saat player bergerak

### Langkah 6: Test Jalan-Jalan
- Saat Play mode, gerakkan player ke segala arah
- Chunk baru akan muncul di depan, chunk lama menghilang di belakang
- **Tidak ada ujung dunia!** 🎉

---

## Perbandingan View Distance

| View Distance | Grid | Total Chunk | Area Terlihat |
|---------------|------|------------|---------------|
| 2 | 5×5 | 25 | 1280×1280m |
| 3 | 7×7 | 49 | 1792×1792m |
| **4** | **9×9** | **81** | **2304×2304m** |
| 6 | 13×13 | 169 | 3328×3328m |
| 8 | 17×17 | 289 | 4352×4352m |

> ⚠️ View distance tinggi = lebih banyak chunk di-generate = lebih lambat.
> Mulai dari **4**, naikkan jika performa masih baik.

---

## Struktur File

```
ProceduralTerrain/
├── Core/
│   ├── NoiseGenerator.cs          — Generate() untuk single + GenerateRegion() untuk chunk
│   ├── NoiseSettings.cs           — ScriptableObject preset noise
│   ├── SplineMapper.cs            — Evaluasi kurva mapping
│   └── HeightMapProcessor.cs      — Smoothing, falloff, clamp
├── Layers/                        ← Dipakai bersama oleh kedua mode!
│   ├── ContinentalnessLayer.cs
│   ├── ErosionLayer.cs
│   ├── PeaksValleysLayer.cs
│   ├── RiverCarverLayer.cs
│   ├── LakeBasinLayer.cs
│   └── BadlandsLayer.cs
├── TerrainPipeline.cs             ← Mode 1: single terrain
├── InfiniteTerrainManager.cs      ← Mode 2: infinite chunks  
└── Editor/
    ├── TerrainPipelineEditor.cs
    └── InfiniteTerrainManagerEditor.cs
```

---

## Tips & Troubleshooting

### Terrain terlalu datar?
- Pastikan **Terrain Height ≥ 300** (di parameter, bukan TerrainData)
- Cek Console Unity untuk log statistik heightmap

### Chunk tidak muncul saat Play?
- Pastikan **Player** sudah di-assign di Inspector
- Player harus ada di scene (bukan null)

### Performa lambat?
- Kurangi **View Distance** (4 → 3 atau 2)
- Kurangi **Heightmap Resolution** (257 → 129)
- Kurangi **Smooth Passes** (5 → 2)

### Ada celah/patahan antar chunk?
- Ini seharusnya tidak terjadi — noise menggunakan koordinat absolut
- Coba klik **🔄 Regenerate Same Seed** untuk refresh neighbors
