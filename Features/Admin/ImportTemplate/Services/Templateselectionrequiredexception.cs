namespace STTproject.Features.Admin.ImportTemplate.Services;

public sealed class TemplateSelectionRequiredException : InvalidOperationException
{
    public string ImportType { get; }
    public IReadOnlyList<string> CandidateNames { get; }

    public TemplateSelectionRequiredException(string importType, IReadOnlyList<string> candidateNames)
        : base($"More than one active {importType} template could apply ({string.Join(", ", candidateNames.Select(n => $"\"{n}\""))}). " +
               "A template has to be chosen before this file can be imported.")
    {
        ImportType = importType;
        CandidateNames = candidateNames;
    }
}