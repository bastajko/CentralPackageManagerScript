using System;
using System.Buffers.Text;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;

var watch = System.Diagnostics.Stopwatch.StartNew();
string slnPath = args[0];
string slnFolderPath = calculateSlnFolderPath(slnPath);
Console.WriteLine("Folder Path: "+slnFolderPath);
IList<string> csProjPaths = new List<string>(50);
Dictionary<string, string> nameVersionMap = new Dictionary<string, string>(1000);

// Check if the file exists
if (File.Exists(slnPath))
{
    // Read all lines from the file
    string[] lines = File.ReadAllLines(slnPath);

    Console.WriteLine("Projects relative paths in the solution:");

    // Display the file contents
    foreach (string line in lines)
    {
        if (line.Length > 7 && string.Equals(line.Substring(0, 7), "Project"))
        {
            string[] lineTokens = line.Split('"', StringSplitOptions.RemoveEmptyEntries);
            foreach(string token in lineTokens)
            {
                if (token[0].Equals('.'))
                {
                    string[] miniTokens = token.Split('.', StringSplitOptions.RemoveEmptyEntries);
                    if (!miniTokens[miniTokens.Length - 1].Equals("sfproj"))
                    {
                        var fullCsProjPath = slnFolderPath + token;
                        csProjPaths.Add(fullCsProjPath);
                        readCsProjFile(fullCsProjPath);
                        Console.WriteLine(token);
                    }
                    else
                    {
                        continue;
                    }
                }
                else
                {
                    continue;
                }
            }
        }
        else
        {
            continue;
        }
    }
}
else
{
    Console.WriteLine("File not found.");
}

writeDictionary();

createDirectoryPackagesPropsFile();

modifyCsProjFiles();

watch.Stop();
var elapsedMs = watch.ElapsedMilliseconds;
Console.WriteLine("Time taken: " + elapsedMs + " ms");


void readCsProjFile(string path)
{
    // Load the .csproj XML file
    XDocument projectFile = XDocument.Load(path);

    // Get all PackageReference elements
    var packageReferences = projectFile.Descendants("PackageReference")
        .Select(pr => new
        {
            Name = pr.Attribute("Include")?.Value,
            Version = pr.Attribute("Version")?.Value
        });

    // Output the package names and versions
    foreach (var package in packageReferences)
    {
        if(string.IsNullOrEmpty(package.Name))
        {
            Console.WriteLine("Package name is empty");
            continue;
        }
        else if (string.IsNullOrEmpty(package.Version))
        {
            Console.WriteLine($"Package {package.Name} has no version");
            continue;
        }

        if (nameVersionMap.ContainsKey(package.Name))
        {
            var result = isPackageRefVersionHigher(package.Name, package.Version);
            if (result > 0)
            {
                Console.WriteLine("versionInReference is greater");
                nameVersionMap[package.Name] = package.Version;
            }
        }
        else
        {
            nameVersionMap.Add(package.Name, package.Version);
        }
    }

}

string calculateSlnFolderPath(string slnPath)
{
    string retPath = "";
    int lastInd = slnPath.LastIndexOf("\\");
    if (lastInd >= 0)
        retPath = slnPath.Substring(0, lastInd+1);
    return retPath;
}

void writeDictionary()
{
    Console.WriteLine();
    Console.WriteLine();
    Console.WriteLine("Package names and versions:");
    Console.WriteLine();
    foreach (var kvp in nameVersionMap)
    {
        Console.WriteLine($"{kvp.Key} : {kvp.Value}");
    }
}

void createDirectoryPackagesPropsFile()
{
    // Create the root element <Project>
    var projectElement = new XElement("Project",
        new XElement("PropertyGroup",
            new XElement("ManagePackageVersionsCentrally", "true")
        ),
        new XElement("ItemGroup",
            // Add each package from the dictionary
            GeneratePackageElements(nameVersionMap)
        )
    );

    // Create the XDocument with the root element
    var document = new XDocument(projectElement);

    // Save the XML file to the specified output path
    document.Save(slnFolderPath + "\\Directory.Packages.props");

    Console.WriteLine($"XML file saved to: {slnFolderPath + "\\Directory.Packages.props"}");
}

// Method to create <PackageVersion Include="..." Version="..."/> elements
static IEnumerable<XElement> GeneratePackageElements(Dictionary<string, string> packages)
{
    foreach (var package in packages)
    {
        yield return new XElement("PackageVersion",
            new XAttribute("Include", package.Key),
            new XAttribute("Version", package.Value)
        );
    }
}

void modifyCsProjFiles()
{
    foreach(var path in csProjPaths)
    {
        // Load the .csproj XML file
        XDocument projectFile = XDocument.Load(path);

        // Get all PackageReference elements
        // Find all <PackageReference> elements
        var packageReferences = projectFile.Descendants("PackageReference");
        if (packageReferences == null)
        {
            Console.WriteLine("No PackageReference elements found in the file");
            continue;
        }

        foreach (var packageReference in packageReferences)
        {
            // Find the Version attribute
            var versionAttribute = packageReference.Attribute("Version");
            var lowercaseVersionAttribute = packageReference.Attribute("version");
            if (versionAttribute == null && lowercaseVersionAttribute == null)
            {
                continue;
            }
            bool isUpperCase = versionAttribute != null;

            string packageVersion = isUpperCase ? versionAttribute.Value.ToString() : lowercaseVersionAttribute.Value.ToString();
            string packageName = packageReference.Attribute("Include")?.Value?.ToString() ?? "";

            if (string.IsNullOrEmpty(packageReference.Name.ToString()))
            {
                Console.WriteLine("Error package name is empty");
                continue;
            }
            else if (string.IsNullOrEmpty(packageVersion))
            {
                Console.WriteLine($"Error package {packageName} has no version");
                continue;
            }

            if (nameVersionMap.ContainsKey(packageName))
            {
                var result = isPackageRefVersionHigher(packageName, packageVersion);
                if (result != 0)
                {
                    packageReference.SetAttributeValue("VersionOverride", packageVersion);
                }
                if (isUpperCase)
                {
                    versionAttribute.Remove();
                }
                else
                {
                    lowercaseVersionAttribute.Remove();
                }
                packageReference.Name = "PackageVersion";
            }
            else
            {
                Console.WriteLine($"Error, missing a {packageName} package reference in Dictionary!");
            }
        }
        projectFile.Save(path);
    }
}

int isPackageRefVersionHigher(string packageName, string packageVersion)
{
    if (char.Equals(packageVersion[0], '$'))
    {
        return 0;
    }
    Console.WriteLine($"{packageName}, {packageVersion}, {nameVersionMap[packageName]}");

    int packageSlashIndex = packageVersion.IndexOf("-");
    int nameVersionMapSlashIndex = nameVersionMap[packageName].IndexOf("-");

    var versionInReference = new Version(packageSlashIndex == -1 ? packageVersion : packageVersion.Substring(0, packageSlashIndex));
    var highestVersionInDictionary = new Version(nameVersionMapSlashIndex == -1 ? nameVersionMap[packageName] : nameVersionMap[packageName].Substring(0, nameVersionMapSlashIndex));

    return versionInReference.CompareTo(highestVersionInDictionary);
}