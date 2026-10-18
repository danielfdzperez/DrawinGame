using OpenCvSharp;

namespace DrawingGame.Vision;

/// <summary>
/// Estabiliza las formas detectadas entre fotogramas para eliminar el temblor por ruido de cámara,
/// sin añadir retardo cuando la forma se mueve de verdad.
///
/// Para cada forma estable guardada, al llegar una nueva detección:
///  1. Cambio pequeño (todos los vértices dentro de la tolerancia): se IGNORA, la forma no se mueve.
///  2. Movimiento global (la mitad o más de los vértices se han desplazado más que la tolerancia):
///     la forma se mueve de verdad -> se acepta AL INSTANTE.
///  3. Cambio local (solo algunos vértices se han movido): puede ser ruido de un vértice suelto;
///     solo se acepta si se mantiene unos pocos fotogramas.
/// Las formas nuevas aparecen tras pocos fotogramas y desaparecen tras faltar pocos.
/// </summary>
public class ShapeStabilizer
{
    private const int MatchDistance = 40;      // px: distancia máx. (Hausdorff) para ser "la misma forma"
    private const double MaxCentroidJump = 150; // px: salto máx. de centro entre fotogramas (objeto rápido)
    private const double AreaRatioMin = 0.6;    // rango de área permitido al reasociar por posición
    private const double AreaRatioMax = 1.6;
    private const double MotionFraction = 0.5;  // fracción de vértices movidos para considerar movimiento global
    private const int AppearFrames = 2;         // fotogramas seguidos para que una forma nueva aparezca
    private const int LocalChangeFrames = 3;    // fotogramas seguidos para aceptar un cambio local
    private const int DropFrames = 3;           // fotogramas sin verla antes de eliminarla

    private class Track
    {
        public DetectedShape Stable = null!;
        public DetectedShape? Candidate;
        public int CandidateCount;
        public int Seen = 1;
        public int Missed;
        public bool Confirmed;
    }

    private readonly List<Track> _tracks = new();

    public void Reset() => _tracks.Clear();

    /// <param name="tolerance">Tolerancia en píxeles. 0 = sin estabilizar.</param>
    public List<DetectedShape> Update(List<DetectedShape> detected, double tolerance)
    {
        if (tolerance <= 0)
        {
            _tracks.Clear();
            return detected;
        }

        var trackUsed = new bool[_tracks.Count];
        var detUsed = new bool[detected.Count];

        // ---- Pasada 1: asociación por parecido de forma (Hausdorff) ----
        var pairs = new List<(double d, int t, int s)>();
        var detBoxes = detected.Select(s => Cv2.BoundingRect(s.Outline)).ToArray();
        for (int t = 0; t < _tracks.Count; t++)
        {
            var tb = Cv2.BoundingRect(_tracks[t].Stable.Outline);
            for (int s = 0; s < detected.Count; s++)
            {
                if (!Overlaps(tb, detBoxes[s], MatchDistance)) continue;
                double d = Distance(_tracks[t].Stable, detected[s]);
                if (d <= MatchDistance) pairs.Add((d, t, s));
            }
        }
        foreach (var (d, t, s) in pairs.OrderBy(p => p.d))
        {
            if (trackUsed[t] || detUsed[s]) continue;
            trackUsed[t] = detUsed[s] = true;
            ApplyMatch(_tracks[t], detected[s], d, tolerance);
        }

        // ---- Pasada 2: objetos que se han movido mucho -> reasociar por posición y tamaño ----
        var jumpPairs = new List<(double dist, int t, int s)>();
        for (int t = 0; t < _tracks.Count; t++)
        {
            if (trackUsed[t]) continue;
            var (tc, ta) = CentroidArea(_tracks[t].Stable);
            for (int s = 0; s < detected.Count; s++)
            {
                if (detUsed[s]) continue;
                var (dc, da) = CentroidArea(detected[s]);
                double dist = Math.Sqrt((tc.X - dc.X) * (tc.X - dc.X) + (tc.Y - dc.Y) * (tc.Y - dc.Y));
                double ratio = ta > 0 ? da / ta : 0;
                if (dist <= MaxCentroidJump && ratio >= AreaRatioMin && ratio <= AreaRatioMax)
                    jumpPairs.Add((dist, t, s));
            }
        }
        foreach (var (dist, t, s) in jumpPairs.OrderBy(p => p.dist))
        {
            if (trackUsed[t] || detUsed[s]) continue;
            trackUsed[t] = detUsed[s] = true;

            // Movimiento rápido: se acepta al instante
            var track = _tracks[t];
            track.Stable = detected[s];
            track.Candidate = null;
            track.CandidateCount = 0;
            track.Missed = 0;
            track.Seen++;
            if (track.Seen >= AppearFrames) track.Confirmed = true;
        }

        // ---- Pistas sin detección: contar fallos y eliminar las que llevan poco sin verse ----
        for (int t = _tracks.Count - 1; t >= 0; t--)
        {
            if (trackUsed[t]) continue;
            _tracks[t].Missed++;
            _tracks[t].Candidate = null;
            _tracks[t].CandidateCount = 0;
            if (_tracks[t].Missed >= DropFrames) _tracks.RemoveAt(t);
        }

        // ---- Detecciones sin pista: formas nuevas ----
        for (int s = 0; s < detected.Count; s++)
        {
            if (detUsed[s]) continue;
            _tracks.Add(new Track { Stable = detected[s], Confirmed = AppearFrames <= 1 });
        }

        return _tracks.Where(t => t.Confirmed).Select(t => t.Stable).ToList();
    }

    private static void ApplyMatch(Track track, DetectedShape det, double d, double tolerance)
    {
        track.Missed = 0;
        track.Seen++;
        if (track.Seen >= AppearFrames) track.Confirmed = true;

        // 1) Ruido: la forma se mantiene exactamente igual
        if (d <= tolerance)
        {
            track.Candidate = null;
            track.CandidateCount = 0;
            return;
        }

        // 2) Movimiento global: muchos vértices desplazados -> aceptar al instante
        double moved = Math.Max(MovedFraction(track.Stable, det, tolerance),
                                MovedFraction(det, track.Stable, tolerance));
        if (moved >= MotionFraction)
        {
            track.Stable = det;
            track.Candidate = null;
            track.CandidateCount = 0;
            return;
        }

        // 3) Cambio local (p. ej. un vértice suelto): solo si se mantiene unos fotogramas
        if (track.Candidate != null && Distance(track.Candidate, det) <= tolerance)
        {
            track.CandidateCount++;
            track.Candidate = det;
            if (track.CandidateCount >= LocalChangeFrames)
            {
                track.Stable = det;
                track.Candidate = null;
                track.CandidateCount = 0;
            }
        }
        else
        {
            track.Candidate = det;
            track.CandidateCount = 1;
        }
    }

    private static bool Overlaps(Rect a, Rect b, int margin) =>
        a.Left - margin < b.Right && b.Left - margin < a.Right &&
        a.Top - margin < b.Bottom && b.Top - margin < a.Bottom;

    private static (Point2d centroid, double area) CentroidArea(DetectedShape s)
    {
        var m = Cv2.Moments(s.Outline);
        if (Math.Abs(m.M00) < 1e-6)
        {
            double mx = s.Outline.Average(p => (double)p.X);
            double my = s.Outline.Average(p => (double)p.Y);
            return (new Point2d(mx, my), 0);
        }
        return (new Point2d(m.M10 / m.M00, m.M01 / m.M00), Math.Abs(m.M00));
    }

    /// <summary>Fracción de vértices de 'from' que están a más de 'tolerance' de la forma 'to'.</summary>
    private static double MovedFraction(DetectedShape from, DetectedShape to, double tolerance)
    {
        var rings = Rings(to);
        int total = 0, moved = 0;
        foreach (var ring in Rings(from))
        {
            foreach (var p in ring)
            {
                total++;
                if (DistanceToRings(p, rings) > tolerance) moved++;
            }
        }
        return total == 0 ? 0 : (double)moved / total;
    }

    /// <summary>Distancia de Hausdorff aproximada (en píxeles) entre dos formas, contando contorno y huecos.</summary>
    private static double Distance(DetectedShape a, DetectedShape b)
    {
        var ra = Rings(a);
        var rb = Rings(b);
        return Math.Max(Directed(ra, rb), Directed(rb, ra));
    }

    private static double Directed(List<OpenCvSharp.Point[]> from, List<OpenCvSharp.Point[]> to)
    {
        double max = 0;
        foreach (var ring in from)
            foreach (var p in ring)
            {
                double best = DistanceToRings(p, to);
                if (best > max) max = best;
            }
        return max;
    }

    private static double DistanceToRings(OpenCvSharp.Point p, List<OpenCvSharp.Point[]> rings)
    {
        var pf = new Point2f(p.X, p.Y);
        double best = double.MaxValue;
        foreach (var r in rings)
        {
            double d = Math.Abs(Cv2.PointPolygonTest(r, pf, true));
            if (d < best) best = d;
        }
        return best;
    }

    private static List<OpenCvSharp.Point[]> Rings(DetectedShape s)
    {
        var list = new List<OpenCvSharp.Point[]> { s.Outline };
        list.AddRange(s.Holes);
        return list;
    }
}
