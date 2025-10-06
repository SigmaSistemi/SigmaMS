using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using System;
using System.Windows.Media;

namespace SigmaMS.Editor {
    public class SqlCompletionData : ICompletionData {
        public SqlCompletionData(string text, string description = null, CompletionType type = CompletionType.Unknown) {
            Text = text;
            Description = description ?? text;
            CompletionType = type;

            // Imposta priorità e icona in base al tipo
            Priority = GetPriorityForType(type);

            // Imposta il contenuto per la visualizzazione (con icona)
            Content = GetContentWithIcon();
        }

        public ImageSource Image => null; // Usiamo testo invece di immagini

        public string Text { get; private set; }

        public object Content { get; private set; }

        public object Description { get; private set; }

        public double Priority { get; private set; }

        public CompletionType CompletionType { get; private set; }

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) {
            // Trova l'inizio della parola corrente per sostituirla completamente
            var document = textArea.Document;
            var offset = textArea.Caret.Offset;

            // Trova l'inizio della parola corrente
            var startOffset = offset;
            while (startOffset > 0) {
                var prevChar = document.GetCharAt(startOffset - 1);
                if (!char.IsLetterOrDigit(prevChar) && prevChar != '_') {
                    break;
                }
                startOffset--;
            }

            // Trova la fine della parola corrente (se c'è)
            var endOffset = offset;
            while (endOffset < document.TextLength) {
                var currentChar = document.GetCharAt(endOffset);
                if (!char.IsLetterOrDigit(currentChar) && currentChar != '_') {
                    break;
                }
                endOffset++;
            }

            // Sostituisci l'intera parola corrente con il testo selezionato
            var replacementSegment = new TextSegment { StartOffset = startOffset, EndOffset = endOffset };
            document.Replace(replacementSegment, Text);
        }

        private double GetPriorityForType(CompletionType type) {
            return type switch {
                CompletionType.Column => 1.0,
                CompletionType.Table => 0.9,
                CompletionType.View => 0.8,
                CompletionType.StoredProcedure => 0.7,
                CompletionType.Function => 0.6,
                CompletionType.Keyword => 0.5,
                CompletionType.Schema => 0.4,
                _ => 0.1
            };
        }

        private object GetContentWithIcon() {
            var icon = CompletionType switch {
                CompletionType.Table => "🗃️",
                CompletionType.Column => "📝",
                CompletionType.View => "👁️",
                CompletionType.StoredProcedure => "⚙️",
                CompletionType.Function => "🔢",
                CompletionType.Keyword => "🔤",
                CompletionType.Schema => "📁",
                CompletionType.Trigger => "⚡",
                _ => "📄"
            };

            return $"{icon} {Text}";
        }
    }

    public enum CompletionType {
        Unknown,
        Keyword,
        Table,
        Column,
        View,
        StoredProcedure,
        Function,
        Schema,
        Trigger
    }
}