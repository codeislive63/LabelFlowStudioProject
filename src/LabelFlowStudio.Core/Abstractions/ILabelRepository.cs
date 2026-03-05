using LabelFlowStudio.Core.Models;

namespace LabelFlowStudio.Core.Abstractions;

public interface ILabelRepository
{
    Task<IReadOnlyList<LabelRecord>> GetByTenamAsync(string tenam, CancellationToken cancellationToken);

    Task<bool> UpdateBruttoByTenamAsync(string tenam, decimal brutto, CancellationToken cancellationToken);
}
