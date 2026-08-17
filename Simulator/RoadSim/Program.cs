using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// ============================================================
// RoadSim — simulator standalone dari RoadNetwork.cs (Unity)
// Mereplikasi pipeline RingAndLSystem TANPA Unity:
//   ring → spokes → inner ring → L-System layer 1 → void fill layer 2
//   → connectNearbyEnds → FixRoad klasifikasi → export ASCII
//
// Ini "saluran untuk menguji" perubahan di RoadNetwork.cs:
// jalankan, lihat ASCII map + stats, bandingkan dengan log asli.
// ============================================================

internal class Vec3 : IEquatable<Vec3>
{
    public int X, Z;
    public Vec3(int x, int z) { X = x; Z = z; }
    public Vec3 Add(int dx, int dz) => new Vec3(X + dx, Z + dz);
    public override bool Equals(object obj) => obj is Vec3 v && Equals(v);
    public bool Equals(Vec3 v) => v != null && X == v.X && Z == v.Z;
    public override int GetHashCode() => HashCode.Combine(X, Z);
    public override string ToString() => $"({X},{Z})";
}

internal class LSystem
{
    public int iterations = 3;
    public double chanceToIgnore = 0.1;
    public Random rng;
    public string axiom = "X";
    public char[] rulesIn = { 'X', 'X' };
    public string[] rulesOut = { "FF[+FX]X", "FF[-FX]X" };
    public float[] rulesChance = { 0.5f, 0.5f };

    public void Init(int seed) => rng = new Random(seed);

    public string Generate()
    {
        string current = axiom;
        for (int i = 0; i < iterations; i++)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in current)
            {
                // chanceToIgnore: skip rule — karakter tetap
                if (rng.NextDouble() < chanceToIgnore) { sb.Append(c); continue; }
                bool applied = false;
                for (int r = 0; r < rulesIn.Length; r++)
                {
                    if (rulesIn[r] == c && rng.NextDouble() < rulesChance[r])
                    {
                        sb.Append(rulesOut[r]); applied = true; break;
                    }
                }
                if (!applied) sb.Append(c);
            }
            current = sb.ToString();
        }
        return current;
    }
}

internal static class Program
{
    // ---- Config (replikasi default scene) ----
    static int citySize = 3000, tileSize = 30; // tileSize 30 — note: Unity menghasilkan 87x87 bukan 100x100
    static int ringInsetCells = 7;
    static bool enableInnerRing = true;
    static int innerRingInsetCells = 4;
    static int lSystemIterations = 3;
    static float lSystemBranchChance = 0.3f;
    static float lSystemChanceToIgnore = 0.3f;
    static int lSystemSeedSpacing = 6;
    static int minimumDistanceBetweenRoads = 3;
    static int numberOfRingEntrances = 6;
    static bool connectNearbyEnds = true;
    static int connectEndsMaxDistance = 3;
    static int secondLayerMaxSeeds = 250;
    static float decay = 0.8f;
    static float stepSizeScale = 0; // auto

    static Random rng;    // ring + main pipeline
    static Random rngLs;  // L-system seeds
    static Random rng2;   // void fill

    // state
    static int tilesPerSide, gridMin, gridMax;
    static int ringMinX, ringMaxX, ringMinZ, ringMaxZ;
    static int innerRingMinX = -999, innerRingMaxX = -999, innerRingMinZ = -999, innerRingMaxZ = -999;
    static HashSet<Vec3> ringCells = new();
    static HashSet<Vec3> innerRoadCells = new();
    static HashSet<Vec3> spokeCells = new();
    static HashSet<Vec3> ringEntrances = new();
    static HashSet<Vec3> spokeAnchors = new();
    static List<Vec3> seedPositions = new();

    // scaled params
    static int scaledEffMargin, scaledNearBand, scaledVoidMinCells, scaledPass2MinComp, scaledPass2MaxSeeds;
    static int interiorCells;
    static int voidTarget;

    static readonly (int dx, int dz)[] Dirs = { (0, 1), (1, 0), (0, -1), (-1, 0) };

    static HashSet<Vec3> HasRoadSnapshot() { var s = new HashSet<Vec3>(ringCells); s.UnionWith(innerRoadCells); return s; }
    static bool IsInsideRingInterior(Vec3 c) => c.X > ringMinX && c.X < ringMaxX && c.Z > ringMinZ && c.Z < ringMaxZ;

    static void Main(string[] args)
    {
        // Override dari CLI: RoadSim <citySize> <seed> [iterations]
        if (args.Length >= 1) citySize = int.Parse(args[0]);
        int seed = args.Length >= 2 ? int.Parse(args[1]) : 12082026;
        if (args.Length >= 3) lSystemIterations = int.Parse(args[2]);

        rng = new Random(seed);
        rngLs = new Random(seed + 99);
        rng2 = new Random(seed + 4242);

        tilesPerSide = citySize / tileSize;
        int halfCells = tilesPerSide / 2;
        gridMin = -halfCells; gridMax = halfCells;

        // ---- 1. Ring road ----
        int autoInset = (int)Math.Round(halfCells * 0.30);
        int inset = Math.Clamp(Math.Min(ringInsetCells, autoInset), 2, halfCells - 4);
        ringMinX = ringMinZ = gridMin + inset;
        ringMaxX = ringMaxZ = gridMax - inset;
        ringCells.Clear();
        for (int i = ringMinX; i <= ringMaxX; i++) { AddRingCell(i, ringMinZ); AddRingCell(i, ringMaxZ); }
        for (int i = ringMinZ; i <= ringMaxZ; i++) { AddRingCell(ringMinX, i); AddRingCell(ringMaxX, i); }

        ComputeScaledParams();

        // ---- 2. Spokes ----
        PlaceSpokes();

        // ---- 3. Inner ring ----
        if (enableInnerRing) PlaceInnerRing();

        // ---- 4. L-System layer 1 ----
        PlaceLSystemTiles();

        // ---- 5. Void fill layer 2 ----
        PlaceSecondLayerFill();

        // ---- 6. Connect nearby ends ----
        if (connectNearbyEnds) ConnectNearbyEnds();

        // ---- 7. Export ----
        ExportRoadMap(seed);

        int largestVoidAfter = 0, emptyAfter = 0;
        foreach (var comp in FindEmptyInteriorComponents()) { emptyAfter += comp.Count; largestVoidAfter = Math.Max(largestVoidAfter, comp.Count); }
        float pct = interiorCells > 0 ? 100f * largestVoidAfter / interiorCells : 0;
        Console.WriteLine($"\n=== FINAL: voids={FindEmptyInteriorComponents().Count}, empty={emptyAfter}, largest={largestVoidAfter} ({pct:F1}% interior, target < 5%) ===");
    }

    static void AddRingCell(int x, int z) => ringCells.Add(new Vec3(x, z));

    static void ComputeScaledParams()
    {
        int interiorW = ringMaxX - ringMinX;
        scaledEffMargin = Math.Clamp((int)Math.Round(interiorW * 0.15f), 3, 4);
        scaledEffMargin = Math.Min(scaledEffMargin, minimumDistanceBetweenRoads);
        scaledNearBand = Math.Clamp((int)Math.Round(interiorW * 0.04f), 1, 3);
        int interiorArea = interiorW * interiorW;
        scaledVoidMinCells = Math.Clamp((int)Math.Round(interiorArea * 0.03f), 16, 300);
        scaledPass2MinComp = Math.Clamp((int)Math.Round(interiorArea * 0.01f), 9, 100);
        scaledPass2MaxSeeds = Math.Clamp((int)Math.Round(interiorW / 6f), 1, 20);
        interiorCells = (ringMaxX - ringMinX - 1) * (ringMaxZ - ringMinZ - 1);
        voidTarget = Math.Max(scaledVoidMinCells, (int)Math.Round(interiorCells * 0.03f));
    }

    static void PlaceSpokes()
    {
        int cx = 0, cz = 0;
        var dirs = new[] {
            (dx: 0, dz: 1,  tx: cx, tz: ringMaxZ),
            (dx: 0, dz: -1, tx: cx, tz: ringMinZ),
            (dx: 1, dz: 0,  tx: ringMaxX, tz: cz),
            (dx: -1, dz: 0, tx: ringMinX, tz: cz),
        };
        foreach (var (dx, dz, tx, tz) in dirs)
        {
            int len = Math.Abs(tx - cx) + Math.Abs(tz - cz);
            if (len <= 0) continue;
            for (int i = 0; i <= len; i++)
            {
                var cell = new Vec3(cx + dx * i, cz + dz * i);
                innerRoadCells.Add(cell);
                spokeCells.Add(cell);
            }
        }
    }

    static void PlaceInnerRing()
    {
        int interiorR = ringMaxX - 0;
        int insetI = Math.Clamp(Math.Min(innerRingInsetCells, (int)Math.Floor(interiorR * 0.4f)), 2, interiorR - 3);
        int irMinX = ringMinX + insetI, irMaxX = ringMaxX - insetI;
        int irMinZ = ringMinZ + insetI, irMaxZ = ringMaxZ - insetI;
        if (irMaxX - irMinX < 4 || irMaxZ - irMinZ < 4) return;

        for (int x = irMinX; x <= irMaxX; x++) PlaceInnerRingCell(x, irMinZ);
        for (int x = irMinX; x <= irMaxX; x++) PlaceInnerRingCell(x, irMaxZ);
        for (int z = irMinZ + 1; z <= irMaxZ - 1; z++) PlaceInnerRingCell(irMinX, z);
        for (int z = irMinZ + 1; z <= irMaxZ - 1; z++) PlaceInnerRingCell(irMaxX, z);

        innerRingMinX = irMinX; innerRingMaxX = irMaxX;
        innerRingMinZ = irMinZ; innerRingMaxZ = irMaxZ;
    }

    static void PlaceInnerRingCell(int x, int z)
    {
        var cell = new Vec3(x, z);
        innerRoadCells.Add(cell);
        spokeCells.Add(cell); // inner ring = jaringan inti, seperti spoke
    }

    // ---- L-System ----
    static LSystem BuildLSystem()
    {
        var ls = new LSystem();
        ls.iterations = lSystemIterations;
        ls.chanceToIgnore = Math.Clamp(lSystemChanceToIgnore, 0.05, 0.2);
        return ls;
    }

    static void PlaceLSystemTiles()
    {
        var lsys = BuildLSystem();
        lsys.Init(12082026); // seed utama

        int innerMin = ringMinX + 2, innerMax = ringMaxX - 2;
        int innerSize = innerMax - innerMin;
        // spacing antar seed = lSystemSeedSpacing cell, min 2 seed/sisi, max 20
        int seedPerSide = Math.Clamp((int)Math.Round(innerSize / (float)lSystemSeedSpacing), 2, 20);
        if (seedPerSide % 2 != 0) seedPerSide++;
        float spacing = (float)innerSize / seedPerSide;
        float gridOffsetX = (float)(rngLs.NextDouble() * spacing);
        float gridOffsetZ = (float)(rngLs.NextDouble() * spacing);

        seedPositions.Clear();
        for (int gx = 0; gx < seedPerSide; gx++)
        for (int gz = 0; gz < seedPerSide; gz++)
        {
            float sx = innerMin + gridOffsetX + spacing * (gx + 0.5f) + (float)(rngLs.NextDouble() * 0.5f);
            float sz = innerMin + gridOffsetZ + spacing * (gz + 0.5f) + (float)(rngLs.NextDouble() * 0.5f);
            var startCell = new Vec3((int)Math.Round(sx), (int)Math.Round(sz));
            if (!IsInsideRingInterior(startCell)) continue;

            int startDir = rngLs.Next(0, 4);
            lsys.Init(12082026 + 5000 + gx * 7919 + gz * 131);
            string sentence = lsys.Generate();

            // batas pohon per spoke
            if (spokeCells.Contains(startCell)) continue;
            seedPositions.Add(startCell);
            PlaceSegmentsWithDecay(sentence, startCell.X, startCell.Z, startDir, 12f, decay, -1, 2);
        }
    }

    // ---- void fill ----
    static void PlaceSecondLayerFill()
    {
        var lsys = BuildLSystem();
        lsys.chanceToIgnore = Math.Clamp(lSystemChanceToIgnore, 0.05, 0.2);
        int maxSeeds = Math.Max(secondLayerMaxSeeds, 250);
        int seedsPlaced = 0;
        int emptyBefore = 0, largestVoidBefore = 0;
        foreach (var comp in FindEmptyInteriorComponents())
        {
            emptyBefore += comp.Count;
            largestVoidBefore = Math.Max(largestVoidBefore, comp.Count);
        }
        int emptyProgress = 0;

        for (int iter = 0; iter < maxSeeds; iter++)
        {
            // 1. Komponen kosong terbesar
            HashSet<Vec3> largest = null;
            foreach (var comp in FindEmptyInteriorComponents())
                if (largest == null || comp.Count > largest.Count) largest = comp;
            if (largest == null || largest.Count < voidTarget) break;

            Vec3 seed = DeepestCellInComponent(largest, HasRoadSnapshot());
            if (!IsInsideRingInterior(seed)) break;

            lsys.Init(12082026 + 5000 + iter * 7919);
            string sentence = lsys.Generate();
            int before = innerRoadCells.Count;
            int startDir = rng2.Next(0, 4);

            // Coba 4 arah berbeda untuk memaksimalkan coverage void
            int normalIter = lsys.iterations;
            int placed = 0;
            for (int dir = 0; dir < 4 && placed == 0; dir++)
            {
                lsys.Init(12082026 + 5000 + iter * 7919 + dir * 31);
                string sent = lsys.Generate();
                PlaceSegmentsWithDecay(sent, seed.X, seed.Z, dir, 12f, decay, 2, 3, skipInnerRing: true);
                placed = innerRoadCells.Count - before;
            }
            // Percobaan terakhir: margin lebih kecil jika semua arah gagal
            if (placed == 0)
            {
                lsys.iterations = 1;
                lsys.Init(12082026 + 5000 + iter * 7919);
                string sent = lsys.Generate();
                PlaceSegmentsWithDecay(sent, seed.X, seed.Z, rng2.Next(0, 4), 12f, decay, scaledEffMargin - 1, 3, skipInnerRing: true);
                lsys.iterations = normalIter;
                placed = innerRoadCells.Count - before;
            }

            if (placed == 0) { if (++emptyProgress >= 20) break; continue; }
            emptyProgress = 0;
            seedPositions.Add(seed);
            seedsPlaced++;
        }

        // Pass kedua
        {
            var roadSnap = HasRoadSnapshot();
            int passSeeds = 0;
            foreach (var comp in FindEmptyInteriorComponents())
            {
                if (passSeeds >= scaledPass2MaxSeeds) break;
                if (comp.Count < scaledPass2MinComp) continue;
                Vec3 seed2 = DeepestCellInComponent(comp, roadSnap);
                if (!IsInsideRingInterior(seed2)) continue;
                lsys.Init(12082026 + 9000 + passSeeds * 3571);
                lsys.iterations = 1;
                string sent2 = lsys.Generate();
                PlaceSegmentsWithDecay(sent2, seed2.X, seed2.Z, rng2.Next(0, 4), 12f, decay, 2, 3, skipInnerRing: true);
                passSeeds++; seedsPlaced++;
                roadSnap = HasRoadSnapshot();
            }
        }

        int emptyAfter = 0, largestVoidAfter = 0;
        foreach (var comp in FindEmptyInteriorComponents())
        {
            emptyAfter += comp.Count;
            largestVoidAfter = Math.Max(largestVoidAfter, comp.Count);
        }
        float pct = interiorCells > 0 ? 100f * largestVoidAfter / interiorCells : 0;
        Console.WriteLine($"SecondLayerFill: {seedsPlaced} seed ekstra di void (target {voidTarget} cell), empty {emptyBefore} → {emptyAfter} cell, void terbesar {largestVoidBefore} → {largestVoidAfter} ({pct:F1}% interior, target < 5%)");
    }

    static List<HashSet<Vec3>> FindEmptyInteriorComponents()
    {
        var result = new List<HashSet<Vec3>>();
        var visited = new HashSet<Vec3>();
        var snap = HasRoadSnapshot();
        for (int x = ringMinX + 1; x < ringMaxX; x++)
        for (int z = ringMinZ + 1; z < ringMaxZ; z++)
        {
            var cell = new Vec3(x, z);
            if (snap.Contains(cell) || !visited.Add(cell)) continue;
            var comp = new HashSet<Vec3> { cell };
            var queue = new Queue<Vec3>();
            queue.Enqueue(cell);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var (dx, dz) in Dirs)
                {
                    var nb = cur.Add(dx, dz);
                    if (nb.X <= ringMinX || nb.X >= ringMaxX) continue;
                    if (nb.Z <= ringMinZ || nb.Z >= ringMaxZ) continue;
                    if (snap.Contains(nb) || !visited.Add(nb)) continue;
                    comp.Add(nb); queue.Enqueue(nb);
                }
            }
            result.Add(comp);
        }
        return result;
    }

    static Vec3 DeepestCellInComponent(HashSet<Vec3> comp, HashSet<Vec3> snap)
    {
        var dist = new Dictionary<Vec3, int>();
        var queue = new Queue<Vec3>();
        foreach (var c in comp)
        {
            foreach (var (dx, dz) in Dirs)
            {
                if (snap.Contains(c.Add(dx, dz))) { dist[c] = 0; queue.Enqueue(c); break; }
            }
        }
        Vec3 best = null; int bestDist = -1;
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            int d = dist[cur];
            if (d > bestDist) { bestDist = d; best = cur; }
            foreach (var (dx, dz) in Dirs)
            {
                var nb = cur.Add(dx, dz);
                if (!comp.Contains(nb) || dist.ContainsKey(nb)) continue;
                dist[nb] = d + 1; queue.Enqueue(nb);
            }
        }
        if (best != null) return best;
        foreach (var c in comp) return c; // fallback
        return null;
    }

    // ---- PlaceSegmentsWithDecay (inti) ----
    static int PlaceSegmentsWithDecay(string sentence, int startX, int startZ, int startDir,
                                      float baseStep, float decayFactor, int clearanceMargin, int hierarchy,
                                      bool skipInnerRing = false)
    {
        int placed = 0;
        var turtle = new RoadTurtle(startX, startZ, startDir, baseStep);
        var stack = new Stack<(int x, int z, int dir, float step)>();
        float curStep = baseStep;

        // Snapshot dibuat SEKALI di awal, di-update incremental setiap cell
        // di-place — menghindari O(n²) karena HasRoadSnapshot() membuat
        // HashSet baru setiap panggilan.
        var snap = HasRoadSnapshot();

        for (int si = 0; si < sentence.Length; si++)
        {
            char c = sentence[si];
            switch (c)
            {
                case 'F':
                {
                    if (curStep <= 0) break;
                    int steps = Math.Max(1, (int)Math.Round(curStep / tileSize));
                    int fx = turtle.X, fz = turtle.Z;
                    int dx = turtle.DX, dz = turtle.DZ;
                    int toX = fx + dx * steps, toZ = fz + dz * steps;

                    // clamp ke ring (connect-and-stop)
                    bool hitRing = ClampToRing(ref toX, ref toZ, fx, fz);
                    bool hitSpoke = !hitRing && IsSpoke(toX, toZ);

                    var dir = new Vec3(Math.Sign(toX - fx), Math.Sign(toZ - fz));
                    int len = Math.Max(Math.Abs(toX - fx), Math.Abs(toZ - fz));
                    if (len <= 0) break;

                    // ---- PathMinClearance (pakai snap yang sudah ada) ----
                    bool closeToNetwork = hitRing || hitSpoke
                        || ringCells.Contains(new Vec3(fx, fz))
                        || spokeCells.Contains(new Vec3(fx, fz));
                    int effMargin = clearanceMargin >= 1 ? clearanceMargin : scaledEffMargin;
                    if (PathMinClearance(new Vec3(fx, fz), new Vec3(toX, toZ), dir,
                                        effMargin, closeToNetwork, skipInnerRing, snap))
                    {
                        turtle.X = toX; turtle.Z = toZ;
                        curStep = Math.Max(curStep - 2f * tileSize, tileSize);
                        break;
                    }

                    // place — tambah ke innerRoadCells DAN update snap lokal
                    for (int i = 0; i <= len; i++)
                    {
                        var cell = new Vec3(fx + dir.X * i, fz + dir.Z * i);
                        if (innerRoadCells.Add(cell))
                        {
                            snap.Add(cell); // update incremental supaya segmen berikutnya tahu
                            placed++;
                        }
                    }
                    if (hitRing) ringEntrances.Add(new Vec3(toX, toZ));
                    turtle.X = toX; turtle.Z = toZ;
                    break;
                }
                case '+': turtle.Rotate(1); break;
                case '-': turtle.Rotate(-1); break;
                case '|': turtle.Rotate(2); break;
                case '[': stack.Push((turtle.X, turtle.Z, turtle.Dir, curStep)); break;
                case ']':
                    if (stack.Count > 0)
                    {
                        var (sx, sz, sd, ss) = stack.Pop();
                        turtle.X = sx; turtle.Z = sz; turtle.Dir = sd; curStep = ss;
                    }
                    break;
            }
        }
        return placed;
    }

    static bool ClampToRing(ref int toX, ref int toZ, int fx, int fz)
    {
        if (toX <= ringMinX || toX >= ringMaxX || toZ <= ringMinZ || toZ >= ringMaxZ)
        {
            // potong segmen di perbatasan ring
            int dx = Math.Sign(toX - fx), dz = Math.Sign(toZ - fz);
            int steps = 0;
            while (true)
            {
                int nx = fx + dx * steps, nz = fz + dz * steps;
                if (nx < ringMinX || nx > ringMaxX || nz < ringMinZ || nz > ringMaxZ) break;
                toX = nx; toZ = nz; steps++;
            }
            return true;
        }
        return false;
    }

    static bool IsSpoke(int x, int z) => spokeCells.Contains(new Vec3(x, z));

    static bool PathMinClearance(Vec3 from, Vec3 to, Vec3 dir, int margin, bool skipsToRingSeparate, bool skipInnerRing, HashSet<Vec3> snap)
    {
        if (margin <= 1) return false;
        int minX = Math.Min(from.X, to.X) - margin, maxX = Math.Max(from.X, to.X) + margin;
        int minZ = Math.Min(from.Z, to.Z) - margin, maxZ = Math.Max(from.Z, to.Z) + margin;

        foreach (var cell in snap)
        {
            if (cell.X < minX || cell.X > maxX || cell.Z < minZ || cell.Z > maxZ) continue;
            if (ringCells.Contains(cell)) continue;
            if (spokeCells.Contains(cell)) continue;

            // colinear dengan segmen (baris/kolom yang sama)
            if (dir.X != 0 && cell.Z == from.Z) continue;
            if (dir.Z != 0 && cell.X == from.X) continue;

            // crossing: proyeksi ke segmen (round) — kalau cell jalan tsb ada di
            // garis segmen → junction sah, bukan pelanggaran
            if (dir.X != 0)
            {
                int projX = Math.Clamp(cell.X, Math.Min(from.X, to.X), Math.Max(from.X, to.X));
                if (snap.Contains(new Vec3(projX, from.Z)) || new Vec3(projX, from.Z).Equals(from)) continue;
            }
            else if (dir.Z != 0)
            {
                int projZ = Math.Clamp(cell.Z, Math.Min(from.Z, to.Z), Math.Max(from.Z, to.Z));
                if (snap.Contains(new Vec3(from.X, projZ)) || new Vec3(from.X, projZ).Equals(from)) continue;
            }

            if (skipsToRingSeparate && (ringCells.Contains(cell.Add(dir.X, dir.Z)) || spokeCells.Contains(cell.Add(dir.X, dir.Z))))
                continue;

            return true; // ada jalan lain di dekat segmen
        }

        // Fix "TT wall": tolak paralel dengan outer ring
        if (!skipsToRingSeparate)
        {
            bool nearRingH = from.Z <= ringMinZ + scaledNearBand || from.Z >= ringMaxZ - scaledNearBand
                          || to.Z   <= ringMinZ + scaledNearBand || to.Z   >= ringMaxZ - scaledNearBand;
            bool nearRingV = from.X <= ringMinX + scaledNearBand || from.X >= ringMaxX - scaledNearBand
                          || to.X   <= ringMinX + scaledNearBand || to.X   >= ringMaxX - scaledNearBand;
            bool parallelToRingH = dir.X != 0 && nearRingH;
            bool parallelToRingV = dir.Z != 0 && nearRingV;
            if (parallelToRingH || parallelToRingV) return true;

            // Fix 2 (baru): tolak paralel dengan INNER ring
            if (innerRingMinX != -999 && !skipInnerRing)
            {
                bool nearInnerH = from.Z <= innerRingMinZ + scaledNearBand || from.Z >= innerRingMaxZ - scaledNearBand
                               || to.Z   <= innerRingMinZ + scaledNearBand || to.Z   >= innerRingMaxZ - scaledNearBand;
                bool nearInnerV = from.X <= innerRingMinX + scaledNearBand || from.X >= innerRingMaxX - scaledNearBand
                               || to.X   <= innerRingMinX + scaledNearBand || to.X   >= innerRingMaxX - scaledNearBand;
                bool parallelToInnerH = dir.X != 0 && nearInnerH;
                bool parallelToInnerV = dir.Z != 0 && nearInnerV;
                if (parallelToInnerH || parallelToInnerV) return true;
            }
        }

        return false;
    }

    // ---- ConnectNearbyEnds ----
    static void ConnectNearbyEnds()
    {
        var snap = HasRoadSnapshot();

        // cari semua endpoint (cell dengan 1 tetangga)
        var ends = new List<Vec3>();
        foreach (var cell in snap)
        {
            if (!IsInsideRingInterior(cell)) continue;
            int n = 0;
            foreach (var (dx, dz) in Dirs)
                if (snap.Contains(cell.Add(dx, dz))) n++;
            if (n == 1) ends.Add(cell);
        }

        for (int i = 0; i < ends.Count; i++)
        {
            for (int j = i + 1; j < ends.Count; j++)
            {
                var a = ends[i]; var b = ends[j];
                int dist = Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z);
                if (dist <= connectEndsMaxDistance)
                {
                    // sambungkan dengan garis lurus
                    int dx = Math.Sign(b.X - a.X), dz = Math.Sign(b.Z - a.Z);
                    int len = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Z - a.Z));
                    bool clear = true;
                    for (int k = 1; k <= len; k++)
                    {
                        if (snap.Contains(a.Add(dx * k, dz * k))) { clear = false; break; }
                    }
                    if (!clear) continue;
                    for (int k = 0; k <= len; k++)
                        innerRoadCells.Add(a.Add(dx * k, dz * k));
                    break;
                }
            }
        }
    }

    // ---- Export ----
    static void ExportRoadMap(int seed)
    {
        int size = ringMaxX - ringMinX + 1 + 6;
        var map = new List<string>();
        var snap = HasRoadSnapshot();

        // bikin array 2D classifier (pakai mask)
        var grid2d = new Dictionary<Vec3, int>();
        foreach (var c in snap)
        {
            if (c.X < ringMinX - 1 || c.X > ringMaxX + 1 || c.Z < ringMinZ - 1 || c.Z > ringMaxZ + 1) continue;
            int mask = 0;
            if (snap.Contains(c.Add(0, 1))) mask |= 1;
            if (snap.Contains(c.Add(1, 0))) mask |= 2;
            if (snap.Contains(c.Add(0, -1))) mask |= 4;
            if (snap.Contains(c.Add(-1, 0))) mask |= 8;
            grid2d[c] = mask;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Road Map — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Simulator  |  citySize: {citySize}  |  tileScale: {tileSize}");
        sb.AppendLine($"Grid: {tilesPerSide} x {tilesPerSide} cells  |  Total road tiles: {snap.Count}");
        sb.AppendLine($"Cell range: X[{gridMin}..{gridMax}]  Z[{gridMin}..{gridMax}]");
        sb.AppendLine($"seed: {seed}  |  iter: {lSystemIterations}  |  effMargin: {scaledEffMargin}  |  nearBand: {scaledNearBand}");
        sb.AppendLine();
        sb.AppendLine("Legenda: + = 4-way  T = 3-way  I = straight  L = corner  O = end  # = empty");
        sb.AppendLine("-----------------------------------------------------------------------------------------");

        for (int z = ringMaxZ + 1; z >= ringMinZ - 1; z--)
        {
            var line = new char[tilesPerSide + 6];
            for (int x = 0; x < line.Length; x++) line[x] = ' ';
            int idx = 0;
            for (int x = ringMinX - 1; x <= ringMaxX + 1; x++)
            {
                var cell = new Vec3(x, z);
                if (grid2d.TryGetValue(cell, out int mask))
                {
                    line[idx] = mask switch
                    {
                        0 => '#',
                        1 or 2 or 4 or 8 => 'O',
                        5 or 10 => 'I',
                        3 or 6 or 9 or 12 => 'L',
                        7 or 11 or 13 or 14 => 'T',
                        15 => '+',
                        _ => '#'
                    };
                }
                else line[idx] = '#';
                idx++;
            }
            int cellIdx = (z + (tilesPerSide / 2));
            string prefix = z == 0 ? "  " : "";
            sb.AppendLine($"{prefix}{new string(line)}");
        }
        sb.AppendLine("-----------------------------------------------------------------------------------------");

        int plus=0, tee=0, strai=0, corner=0, endc=0;
        foreach (var (k, mask) in grid2d)
        {
            switch (mask)
            {
                case 15: plus++; break;
                case 7 or 11 or 13 or 14: tee++; break;
                case 5 or 10: strai++; break;
                case 3 or 6 or 9 or 12: corner++; break;
                case 1 or 2 or 4 or 8: endc++; break;
            }
        }
        sb.AppendLine($"Stats: + {plus}  T {tee}  I {strai}  L {corner}  O {endc}");

        string dir = @"C:\My FIle\Unity\Procedural-Generation-Map-Project\Assets\RoadMapLogs";
        Directory.CreateDirectory(dir);
        // Hapus semua file RoadSim lama sebelum generate baru
        foreach (var old in Directory.GetFiles(dir, "RoadSim_*.txt"))
            File.Delete(old);
        string fname = $"RoadSim_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        string path = Path.Combine(dir, fname);
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine($"Map exported → {fname}");
    }
}

internal class RoadTurtle
{
    public int X, Z, Dir;
    public float StepSize;

    // 0=N, 1=E, 2=S, 3=W
    public static readonly (int dx, int dz)[] DirVec = { (0, 1), (1, 0), (0, -1), (-1, 0) };
    public int DX => DirVec[Dir].dx;
    public int DZ => DirVec[Dir].dz;

    public RoadTurtle(int x, int z, int dir, float step) { X = x; Z = z; Dir = dir; StepSize = step; }
    public void Rotate(int dir) => Dir = ((Dir + dir) % 4 + 4) % 4;
}