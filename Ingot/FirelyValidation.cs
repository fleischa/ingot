namespace Ingot;

public class FirelyValidation
{
	public string Parsing { get; set; } = "Permissive";

	public string Level { get; set; } = "Full";

	public string[] AllowedProfiles { get; set; } = Array.Empty<string>();
}