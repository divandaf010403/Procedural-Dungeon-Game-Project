using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// String-based L-System generator.
///
/// Diadaptasi dari SVS Procedural Town (Sunny Valle Studio):
///   - LSystemGenerator.cs  → GrowRecursive + ProcessRulesRecursively
///   - SimpleVisualizer.cs  → EncodingLetters (F, +, -, [, ])
///
/// Symbols:
///   F  = maju 1 step, place road
///   f  = maju 1 step, no road
///   +  = belok kanan 90°
///   -  = belok kiri 90°
///   |  = balik 180°
///   [  = push state (cabang baru)
///   ]  = pop state (kembali ke parent)
///   X  = growth marker (untuk expansion, tidak di-draw)
/// </summary>
[System.Serializable]
public class LSystemGenerator
{
    [System.Serializable]
    public struct Rule
    {
        public char   input;
        public string output;

        [Range(0f, 1f)]
        public float chance; // probabilitas rule ini dipakai (1 = selalu)
    }

    [Header("L-System Settings")]
    public string axiom          = "X";
    public Rule[] rules;
    public int    iterations     = 4;

    [Range(0f, 1f)]
    [Tooltip("Probabilitas skip rule per-karakter. 0 = deterministik, 0.3 = organik (SVS default).")]
    public float  chanceToIgnore = 0.1f;

    private System.Random rng;

    // -----------------------------------------------------------------------
    // INIT + GENERATE
    // -----------------------------------------------------------------------

    public void Init(int seed)
    {
        rng = new System.Random(seed);
    }

    /// <summary>
    /// Expand axiom sebanyak `iterations` kali menggunakan iterative rewriting.
    /// Berbeda dari SVS GrowRecursive (recursive depth-first), ini pakai
    /// iterative string replacement yang lebih efisien untuk iterasi tinggi.
    /// </summary>
    public string Generate()
    {
        if (rng == null) rng = new System.Random(0);
        string current = axiom;
        for (int i = 0; i < iterations; i++)
            current = Expand(current, i);
        return current;
    }

    private string Expand(string input, int iterationIndex)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in input)
        {
            bool replaced = false;
            foreach (var rule in rules)
            {
                if (rule.input != c) continue;

                // Dari SVS: randomIgnoreRuleModifier aktif setelah iterasi ke-1
                if (iterationIndex > 1 && rng.NextDouble() < chanceToIgnore)
                    continue;

                // Rule chance
                if (rng.NextDouble() > rule.chance) continue;

                sb.Append(rule.output);
                replaced = true;
                break;
            }
            if (!replaced) sb.Append(c);
        }
        return sb.ToString();
    }

    // -----------------------------------------------------------------------
    // STATIC PRESET FACTORIES
    // Terinspirasi dari berbagai pola kota nyata dan SVS example patterns.
    // -----------------------------------------------------------------------

    /// <summary>
    /// OrganicCity — pattern default dari SVS SimpleVisualizer.
    /// Axiom X → F[-FX]+FX menghasilkan percabangan biner organik.
    /// Cocok untuk kota kecil dengan jalan tidak teratur.
    /// </summary>
    public static LSystemGenerator CreateOrganicCityPreset(int iterations = 4, float chanceIgnore = 0.3f)
    {
        var lsys = new LSystemGenerator();
        lsys.axiom          = "X";
        lsys.iterations     = iterations;
        lsys.chanceToIgnore = chanceIgnore;
        lsys.rules = new Rule[]
        {
            new Rule { input = 'X', output = "F[-FX]+FX", chance = 1.0f },
        };
        return lsys;
    }

    /// <summary>
    /// ManhattanGrid — grid orthogonal rapat ala NYC.
    /// Cabang kanan dan kiri di setiap node growth, menghasilkan grid teratur.
    /// chanceIgnore rendah (0.1) agar grid tetap rapi.
    /// </summary>
    public static LSystemGenerator CreateManhattanPreset(int iterations = 3, float chanceIgnore = 0.1f)
    {
        var lsys = new LSystemGenerator();
        lsys.axiom          = "FX";
        lsys.iterations     = iterations;
        lsys.chanceToIgnore = chanceIgnore;
        lsys.rules = new Rule[]
        {
            new Rule { input = 'X', output = "[+FX][-FX]FX", chance = 1.0f },
            new Rule { input = 'F', output = "FF",            chance = 0.3f },
        };
        return lsys;
    }

    /// <summary>
    /// HighwayAndAlley — arterial road panjang lurus + gang pendek di sisi.
    /// FFFX menghasilkan main road yang jauh, cabang [+FX][-FX] = gang.
    /// Mirip kota industri dengan blok panjang dan akses samping.
    /// </summary>
    public static LSystemGenerator CreateHighwayAlleyPreset(int iterations = 4, float chanceIgnore = 0.2f)
    {
        var lsys = new LSystemGenerator();
        lsys.axiom          = "FFFX";
        lsys.iterations     = iterations;
        lsys.chanceToIgnore = chanceIgnore;
        lsys.rules = new Rule[]
        {
            new Rule { input = 'X', output = "FFF[+FX][-FX]X", chance = 1.0f },
            new Rule { input = 'F', output = "FF",              chance = 0.15f },
        };
        return lsys;
    }

    /// <summary>
    /// RadialSprawl — jalan memancar dari pusat seperti kota Eropa.
    /// Triple branch per node menghasilkan pola radial + ring jalan.
    /// Cocok untuk kota medieval atau kota dengan pusat plaza.
    /// </summary>
    public static LSystemGenerator CreateRadialSprawlPreset(int iterations = 3, float chanceIgnore = 0.25f)
    {
        var lsys = new LSystemGenerator();
        lsys.axiom          = "X";
        lsys.iterations     = iterations;
        lsys.chanceToIgnore = chanceIgnore;
        lsys.rules = new Rule[]
        {
            new Rule { input = 'X', output = "F[+FX]F[-FX]F[+FX]X", chance = 1.0f },
            new Rule { input = 'F', output = "FF",                    chance = 0.2f },
        };
        return lsys;
    }

    /// <summary>
    /// Suburban — blok besar, jalan sedikit, dead end banyak.
    /// Cocok untuk area perumahan pinggir kota.
    /// </summary>
    public static LSystemGenerator CreateSuburbanPreset(int iterations = 3, float chanceIgnore = 0.4f)
    {
        var lsys = new LSystemGenerator();
        lsys.axiom          = "FX";
        lsys.iterations     = iterations;
        lsys.chanceToIgnore = chanceIgnore; // tinggi → banyak dead end
        lsys.rules = new Rule[]
        {
            new Rule { input = 'X', output = "FF[+FX]X",  chance = 1.0f },
            new Rule { input = 'F', output = "FFF",        chance = 0.1f },
        };
        return lsys;
    }
}
