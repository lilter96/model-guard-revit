using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace BimPortfolio.Revit;

public static class RevitTransaction
{
    sealed class RejectErrors : IFailuresPreprocessor
    {
        public List<string> Errors { get; } = new List<string>();
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var errors = accessor.GetFailureMessages().Where(m => m.GetSeverity() != FailureSeverity.Warning).ToList();
            foreach (var error in errors) Errors.Add(error.GetDescriptionText());
            // Warnings remain visible to the user; no blanket warning swallowing.
            return errors.Count > 0 ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
    public static void Run(Document document, string name, Action action)
    {
        if (document.IsReadOnly || document.IsModifiable) throw new InvalidOperationException("Document is read-only or already in a transaction.");
        using (var transaction = new Transaction(document, name))
        {
            if (transaction.Start() != TransactionStatus.Started) throw new InvalidOperationException("Cannot start transaction.");
            var failures = new RejectErrors();
            var options = transaction.GetFailureHandlingOptions().SetFailuresPreprocessor(failures)
                .SetClearAfterRollback(true).SetForcedModalHandling(true);
            transaction.SetFailureHandlingOptions(options);
            action();
            if (transaction.Commit() != TransactionStatus.Committed)
                throw new InvalidOperationException("Transaction rolled back. " + string.Join("; ", failures.Errors.Distinct()));
        }
    }
    public static void RequireWritable(Document document, Element element)
    {
        if (document.IsWorkshared && WorksharingUtils.GetCheckoutStatus(document, element.Id) == CheckoutStatus.OwnedByOtherUser)
            throw new InvalidOperationException("Element is owned by another user: " + element.UniqueId);
    }
}
