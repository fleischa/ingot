using System.CommandLine;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Ingot;

public class Program
{
	private const string firelyLicenseFile = "firelyserver-license.json";
	private const string firelyAppSettingsFile = "appsettings.instance.json";
	private const string dockerScriptFile = "docker_run.ps1";

	public static async Task<int> Main(string[] args)
	{
		Option<IEnumerable<string>?> packagesOption = new("--packages", "List of packages containing conformance resources.")
		{
			AllowMultipleArgumentsPerToken = true
		};

		Option<FileInfo?> licenseOption = new("--license", "Firely Server license file.");
		licenseOption.AddAlias("-l");

		Option<string?> nameOption = new("--name", () => "firely.server", "Docker container name.");
		nameOption.AddAlias("-n");

		Option<int?> portOption = new("--port", () => 4080, "Firely Server host port.");
		portOption.AddAlias("-p");

		RootCommand rootCommand = new("Ingot - a tool for setting up Firely Server containers");
		rootCommand.AddOption(packagesOption);
		rootCommand.AddOption(nameOption);
		rootCommand.AddOption(portOption);
		rootCommand.AddOption(licenseOption);

		rootCommand.SetHandler(async (packages, name, port, license) => { await Program.SetupContainer(packages, name!, port!.Value, license); },
			packagesOption,
			nameOption,
			portOption,
			licenseOption);

		return await rootCommand.InvokeAsync(args);
	}

	private static async Task SetupContainer(IEnumerable<string>? packages, string name, int port, FileInfo? license)
	{
		DirectoryInfo importDirectory = new("vonk-import.R4");

		if (importDirectory.Exists)
		{
			importDirectory.Delete(true);
		}

		importDirectory.Create();

		if (packages != null)
		{
			foreach (string package in packages)
			{
				string[] tokens = package.Split('@');
				if (tokens.Length != 2)
				{
					throw new ArgumentException();
				}

				await Program.InstallPackage(tokens[0], tokens[1], importDirectory);
			}
		}

		Program.CleanUpPackages(importDirectory);
		Program.WriteFirelyLicense(license);
		await Program.WriteFirelyAppSettings();
		await Program.WriteDockerScript(name, port);
	}

	private static async Task InstallPackage(string name, string version, DirectoryInfo importDirectory)
	{
		ProcessStartInfo processStartInfo = new("fhir", $"install {name} {version} --here")
		{
			WorkingDirectory = importDirectory.FullName
		};

		Process? fhirProcess = Process.Start(processStartInfo);

		if (fhirProcess != null)
		{
			await fhirProcess.WaitForExitAsync();
		}
	}

	private static void CleanUpPackages(DirectoryInfo importDirectory)
	{
		DirectoryInfo? dependenciesDirectory = importDirectory.EnumerateDirectories("dependencies").FirstOrDefault();

		if (dependenciesDirectory == null)
		{
			throw new InvalidOperationException();
		}

		DirectoryInfo[] packageRootDirectories = dependenciesDirectory.GetDirectories();

		foreach (DirectoryInfo packageRootDirectory in packageRootDirectories)
		{
			if (packageRootDirectory.Name.StartsWith("hl7.fhir.r4.core#"))
			{
				packageRootDirectory.Delete(true);
			}
			else
			{
				DirectoryInfo? packageDirectory = packageRootDirectory.EnumerateDirectories("package").FirstOrDefault();

				if (packageDirectory == null)
				{
					continue;
				}

				DirectoryInfo? examplesDirectory = packageDirectory.EnumerateDirectories("examples").FirstOrDefault();
				examplesDirectory?.Delete(true);
			}
		}
	}

	private static void WriteFirelyLicense(FileInfo? license)
	{
		if (license is { Exists: true })
		{
			File.Copy(license.FullName, Program.firelyLicenseFile, true);
		}
	}

	private static async Task WriteFirelyAppSettings()
	{
		if (File.Exists(Program.firelyAppSettingsFile))
		{
			return;
		}

		FirelyAppSettings firelyAppSettings = new();

		JsonSerializerOptions jsonSerializerOptions = new(JsonSerializerDefaults.General)
		{
			WriteIndented = true
		};

		await using Stream stream = File.Open(Program.firelyAppSettingsFile, FileMode.Create, FileAccess.Write);
		await JsonSerializer.SerializeAsync(stream, firelyAppSettings, jsonSerializerOptions);
	}

	private static async Task WriteDockerScript(string name, int port)
	{
		if (File.Exists(Program.dockerScriptFile))
		{
			return;
		}

		StringBuilder builder = new();

		builder.AppendLine($"docker run -d -p {port}:4080 --name {name} `");
		builder.AppendLine($"-v ${{PWD}}/{Program.firelyLicenseFile}:/app/{Program.firelyLicenseFile} `");
		builder.AppendLine($"-v ${{PWD}}/{Program.firelyAppSettingsFile}:/app/{Program.firelyAppSettingsFile} `");
		builder.AppendLine("-v ${PWD}/vonk-import.R4:/app/vonk-import.R4 `");
		builder.AppendLine("firely/server");

		await using Stream stream = File.Open(Program.dockerScriptFile, FileMode.Create, FileAccess.Write);
		await using StreamWriter writer = new(stream);
		await writer.WriteAsync(builder.ToString());
	}
}