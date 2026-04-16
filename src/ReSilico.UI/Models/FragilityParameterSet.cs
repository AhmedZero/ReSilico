namespace ReSilico.UI.Models;

public class FragilityParameterSet
{
    public string ComponentId { get; set; } = "C-PID-1-1";
    public string EdpName { get; set; } = "PID-1-1";
    public string DamageState { get; set; } = "DS1";
    public double Median { get; set; } = 0.03;
    public double Beta { get; set; } = 0.40;
}
