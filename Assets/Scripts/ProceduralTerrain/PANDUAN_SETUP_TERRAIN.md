# Panduan Penggunaan Advanced Procedural Terrain Generator

Berikut adalah langkah-langkah detail untuk menggunakan script prosedur terrain tingkat tinggi yang telah dibuat ke dalam Scene Unity Anda. Sistem ini terinspirasi dari **sistem biome generasi dunia nyata & Minecraft 1.18+**, memadukan 8 layer matematika fractal untuk menghasilkan dunia yang 100% acak namun serealistis mungkin.

## Langkah 1: Persiapan Objek Terrain
1. Pada jendela **Hierarchy** di Unity, klik kanan dan pilih `3D Object` -> `Terrain`.
2. Sebuah objek bernama **Terrain** akan muncul di scene Anda. Objek ini sudah memiliki komponen `Terrain` dan `Terrain Collider`.

## Langkah 2: Memasang Script
1. Pilih objek **Terrain** yang baru saja Anda buat.
2. Pada jendela **Inspector**, klik tombol `Add Component`.
3. Cari dan tambahkan script **`Procedural Terrain Manager`**.

## Langkah 3: Mengatur Tekstur (Terrain Layers)
Sistem ini menggunakan *Splatmap* otomatis berdasarkan ketinggian (*height*) dan kemirigan (*slope*).
1. Di Inspector pada komponen `Terrain`, klik ikon kuas (Paint Terrain).
2. Di dropdown *brush*, pilih `Paint Texture`.
3. Tambahkan tekstur/material Anda pada `Edit Terrain Layers...` -> `Create Layer`. 
   - *Saran Layer: (0) Pasir/Air, (1) Rumput, (2) Tanah/Batu Jurang, (3) Salju Puncak Gunung*

---

## Langkah 4: Konfigurasi 8 Layer Algoritma Dunia (Inspector)

Rahasia utama dari dunia realistis Anda terletak pada 8 lapis perpaduan Noise matematika di bawah.

### 1. Continentalness (Daratan Dasar)
Bagian ini mengatur tinggi rendahnya tanah secara makro (skala besar), membentuk "benua" dan "lautan" lebar. 
- **Base Scale**: Seberapa lebar benua/pulaunya.
- **Base Height Contribution**: Porsi tinggi keseluruhan dari daratan dasar (`0.3` berarti pinggiran laut dan bukit dasar menggunakan 30% dari total tinggi).

### 1.5 Domain Warping (Distorsi Geografi Organik)
*Fitur baru yang sangat krusial!* Tanpa ini, noise perlin biasa terlihat kaku dan terkadang membentuk pola grid/garis-garis yang membosankan.
- **Warp Scale & Strength**: Membengkokkan koordinat di balik layar, sehingga benua, laut, dan pegunungan akan meliuk-liuk, memuntir secara melengkung menghasilkan bentuk kepulauan dan daratan asimetris layaknya dunia nyata. Menghilangkan pola kotak-kotak buatan komputer sepenuhnya!

### 2. Erosion / Plains Mask (Pembatas Dataran Rata)
Bukannya memotong gunung dengan kaku, sistem ini menggunakan *Noise Masking*.
- **Plains Threshold**: Area di mana angka erosi bumi jatuh di bawah batas ini akan sepenuhnya menjadi **dataran rata (Plains)** yang hidup (tetap mengikuti turun naiknya *Continentalness*). Sangat ideal untuk membangun desa!
- **Mask Blend**: Area transisi/lereng di pinggir Plains sebelum menanjak menjadi pegunungan terjal.

### 3. Mountain Details (Pegunungan)
Gunung-gunung tinggi yang mengerikan *hanya* akan muncul jika area tersebut bukan Plains.
- **Detail Scale & Octaves**: Detail bebatuan gunung yang kasar.
- **Mountain Height Contribution**: Porsi tinggi menjulang yang disumbangkan oleh gunung (misal `0.7` atau 70%).

### 4. Plateaus / Mesas (Daratan Tinggi Rata)
Gunung tidak harus selalu lancip. Sistem ini memotong puncak gunung menjadi rata seperti di film *American Wild West* (Mesa/Plateau).
- **Enable Plateaus**: Centang untuk mengaktifkan.
- **Plateau Flatten Threshold**: Batas tinggi minimal di mana puncak gunung akan diratakan layaknya meja raksasa.

### 5. River System (Sungai Pahat Alami)
Menggunakan modifikasi **Ridged Multifractal** untuk mengubah kurva mulus menjadi tebing berbentuk V panjang yang meliuk-liuk bagai ular.
- **Inovasi Pahat Pegunungan**: Hebatnya sungai ini, jika ia melewati gunung tinggi, sungai tidak akan mendaki gunung! Ia akan **memahat/membelah gunung tersebut secara paksa ke bawah** hingga selevel dengan tanah dasar (Base Height). Menciptakan tebing sungai (峡谷) yang ikonik secara alami.
- **River Threshold & Depth**: Mengatur lebar jalur dan kedalaman palung sungai.

### 6. Lake System (Danau Natural)
Mendeteksi *Low-Frequency Noise Pits* (frekuensi terendah pada titik tertentu) untuk digali menjadi danau/kolam besar di tengah daratan. Berbeda dengan laut lepas, danau ini terbentuk di sela-sela daratan Plains.

### 7. Canyons / Ravines (Jurang Retakan Bumi)
Pernah melihat jurang sangat dalam yang tiba-tiba membelah bumi? Layer ini menggunakan Ridged matematis yang *sangat tipis (Threshold kecil)* dan *sangat dalam (Depth besar)* dipadukan dengan eksponensial V-Shape untuk merobek dan memotong daratan secara instan.

### 8. Meteor Craters (Kawah Berapi / Meteor)
Memanfaatkan algoritma *Voronoi - Cellular Sine* pada perlin noise, sistem akan merender lubang kawah bulat yang langka namun nyata.
- **Matematika Kawah**: Algoritma sinus akan mendorong tanah ke atas membentuk gundukan (Bibir Kawah / `Rim Height`), sebelum akhirnya menukik tajam ke bawah tanah membentuk lubang (Palung / `Crater Depth`).

---

## Langkah 5: Generate & Eksplorasi!
1. Scroll ke paling bawah pada script `Procedural Terrain Manager` dan klik **`Generate / Update Terrain`**.
2. Mainkan angka **Seed** untuk men-generate milyaran tata letak dunia yang 100% unik tanpa henti.
3. Eksplorasi sungai yang membelah tebing jurang gunung, temukan kawah meteor yang tersembunyi jauh di balik hutan, dan bangun kota Anda di dataran (Plains) mulus atau di atas Mesa (Plateau)!
