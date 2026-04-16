namespace ReSilico.UI.Models;

public class LossParameterSet
{
    public string ComponentId { get; set; } = "C-PID-1-1";
    public string DamageState { get; set; } = "DS1";
    public double MedianCost { get; set; } = 50_000;
    public double Beta { get; set; } = 0.50;
}
