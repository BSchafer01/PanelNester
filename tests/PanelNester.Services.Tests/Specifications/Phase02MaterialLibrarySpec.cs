namespace PanelNester.Services.Tests.Specifications;

internal static class Phase02MaterialLibrarySpec
{
    internal static string? ClassifyNameConflict(
        IEnumerable<string> existingNames,
        string candidateName,
        string? currentName = null)
    {
        if (string.Equals(candidateName, currentName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return existingNames.Contains(candidateName, StringComparer.OrdinalIgnoreCase)
            ? "material-name-exists"
            : null;
    }

    internal static string? ClassifyDeleteBlocker(bool materialInUse) =>
        materialInUse ? "material-in-use" : null;

    internal static string? ClassifyImportSelection(
        IEnumerable<string> selectedMaterialNames,
        string importedMaterialName) =>
        selectedMaterialNames.Contains(importedMaterialName, StringComparer.Ordinal)
            ? null
            : "material-not-found";
}
