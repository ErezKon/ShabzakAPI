namespace BL.Models
{
    public class AutoAssignScoringOptions
    {
        public bool HoursAsHours { get; set; } = true;
        public bool OverQualificationDamping { get; set; } = true;
        public double OverQualificationFactor { get; set; } = 0.9;
        public bool DeterministicJitter { get; set; } = true;
        public double JitterEpsilon { get; set; } = 0.01;
        public int? JitterSeed { get; set; }
    }
}
