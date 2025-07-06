using Microsoft.CodeAnalysis;

namespace TrProtocol.SerializerGenerator.Internal.Diagnostics {

    public class DiagnosticException : Exception {
        public Diagnostic Diagnostic;
        public DiagnosticException(Diagnostic diagnostic) {
            Diagnostic = diagnostic;
        }
    }
}
