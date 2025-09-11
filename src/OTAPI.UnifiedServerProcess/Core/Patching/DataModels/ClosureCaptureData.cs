using Mono.Cecil;
using Mono.Cecil.Cil;
using System;

namespace OTAPI.UnifiedServerProcess.Core.Patching.DataModels
{
    public struct ClosureCaptureData
    {
        /// <summary>
        /// If capture the 'this' Parameter, this will be method.Body.ThisParameter
        /// <para>If capture the other Parameter, this will be the target Parameter</para>
        /// <para>If capture the local variable, this will be the local variable</para>
        /// </summary>
        public object CaptureVariable;
        public FieldDefinition CaptureField;
        public ClosureCaptureData(string name, VariableDefinition local) {
            CaptureField = new FieldDefinition(name, FieldAttributes.Public, local.VariableType);
            CaptureVariable = local;
        }
        public ClosureCaptureData(MethodDefinition containingMethod, ParameterDefinition parameter) {
            if (containingMethod.Body.ThisParameter == parameter) {
                CaptureField = new FieldDefinition("<>4__this", FieldAttributes.Public, parameter.ParameterType);
            }
            else {
                CaptureField = new FieldDefinition(parameter.Name ?? throw new Exception("Unexpected: Parameter has no name"), FieldAttributes.Public, parameter.ParameterType);
            }
            CaptureVariable = parameter;
        }
    }
}
