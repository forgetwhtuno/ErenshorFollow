using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace ErenshorFollow
{
    // Runtime proof for the exact installed camera boundary documented by the August 15 evidence.
    // No metadata token is trusted across game versions. If any semantic/member relationship changes,
    // Follow installs no camera patch rather than guessing another native input method.
    internal static class FollowCameraCompatibility
    {
        private static readonly Dictionary<short, OpCode> OpCodesByValue = BuildOpCodeMap();

        internal static string LastFailure { get; private set; }

        internal static bool VerifyUsingUiBoundary(out MethodInfo usingUi)
        {
            usingUi = null;
            LastFailure = null;
            try
            {
                Type cameraType = typeof(CameraController);
                if (cameraType == null)
                    return Fail("CameraController type unavailable", out usingUi);

                MethodInfo candidate = ExactInstanceMethod(cameraType, "UsingUI", typeof(bool));
                if (candidate == null)
                    return Fail("CameraController.UsingUI() bool shape changed", out usingUi);

                FieldInfo uiWindows = cameraType.GetField("UIWindows",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (uiWindows == null || uiWindows.FieldType != typeof(List<GameObject>))
                    return Fail("CameraController.UIWindows List<GameObject> shape changed", out usingUi);

                PropertyInfo activeSelfProperty = typeof(GameObject).GetProperty("activeSelf",
                    BindingFlags.Instance | BindingFlags.Public);
                MethodInfo activeSelf = activeSelfProperty == null ? null : activeSelfProperty.GetGetMethod();
                if (activeSelf == null || !ReferencesMember(candidate, uiWindows) || !ReferencesMember(candidate, activeSelf))
                    return Fail("CameraController.UsingUI no longer scans UIWindows activeSelf", out usingUi);

                MethodInfo update = ExactInstanceMethod(cameraType, "Update", typeof(void));
                MethodInfo modern = ExactInstanceMethod(cameraType, "ModernControls", typeof(void));
                MethodInfo controls = ExactInstanceMethod(cameraType, "Controls", typeof(void));
                if (update == null || modern == null || controls == null)
                    return Fail("CameraController control-method shape changed", out usingUi);
                if (!ReferencesMember(update, modern))
                    return Fail("CameraController.Update no longer references ModernControls", out usingUi);

                FieldInfo releaseMouse = cameraType.GetField("releaseMouse",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (releaseMouse == null || releaseMouse.FieldType != typeof(bool))
                    return Fail("CameraController.releaseMouse bool shape changed", out usingUi);

                MethodInfo getAxis = typeof(Input).GetMethod("GetAxis", BindingFlags.Public | BindingFlags.Static,
                    null, new Type[] { typeof(string) }, null);
                if (getAxis == null || !ReferencesMember(modern, candidate) ||
                    !ReferencesMember(modern, releaseMouse) || !ReferencesMember(modern, getAxis))
                    return Fail("CameraController.ModernControls no longer matches verified UsingUI/releaseMouse/GetAxis boundary", out usingUi);

                FieldInfo dragging = typeof(GameData).GetField("DraggingUIElement",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (dragging == null || dragging.FieldType != typeof(bool) || !ReferencesMember(controls, dragging))
                    return Fail("CameraController.Controls no longer references GameData.DraggingUIElement", out usingUi);

                usingUi = candidate;
                return true;
            }
            catch (Exception ex)
            {
                return Fail("camera boundary proof failed (" + ex.GetType().Name + ")", out usingUi);
            }
        }

        private static MethodInfo ExactInstanceMethod(Type type, string name, Type returnType)
        {
            MethodInfo method = type.GetMethod(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (method == null || method.ReturnType != returnType || method.GetParameters().Length != 0) return null;
            return method;
        }

        private static bool Fail(string failure, out MethodInfo method)
        {
            method = null;
            LastFailure = failure;
            return false;
        }

        private static bool ReferencesMember(MethodInfo method, MemberInfo target)
        {
            if (method == null || target == null) return false;
            MethodBody body;
            byte[] il;
            try
            {
                body = method.GetMethodBody();
                il = body == null ? null : body.GetILAsByteArray();
            }
            catch { return false; }
            if (il == null || il.Length == 0) return false;

            Type[] typeArgs = null;
            Type[] methodArgs = null;
            try
            {
                if (method.DeclaringType != null && method.DeclaringType.IsGenericType)
                    typeArgs = method.DeclaringType.GetGenericArguments();
                if (method.IsGenericMethod) methodArgs = method.GetGenericArguments();
            }
            catch { }

            int offset = 0;
            while (offset < il.Length)
            {
                OpCode opcode;
                if (!TryReadOpCode(il, ref offset, out opcode)) return false;
                int operandSize;
                if (IsMetadataOperand(opcode.OperandType))
                {
                    if (offset + 4 > il.Length) return false;
                    int token = BitConverter.ToInt32(il, offset);
                    MemberInfo resolved = null;
                    try { resolved = method.Module.ResolveMember(token, typeArgs, methodArgs); } catch { }
                    if (SameMember(resolved, target)) return true;
                    operandSize = 4;
                }
                else
                {
                    if (!TryOperandSize(opcode.OperandType, il, offset, out operandSize)) return false;
                }
                if (operandSize < 0 || offset + operandSize > il.Length) return false;
                offset += operandSize;
            }
            return false;
        }

        private static bool SameMember(MemberInfo left, MemberInfo right)
        {
            if (left == null || right == null) return false;
            if (ReferenceEquals(left, right)) return true;
            try { return left.Module == right.Module && left.MetadataToken == right.MetadataToken; }
            catch { return false; }
        }

        private static bool TryReadOpCode(byte[] il, ref int offset, out OpCode opcode)
        {
            opcode = default(OpCode);
            if (offset >= il.Length) return false;
            short value = il[offset++];
            if (value == 0xFE)
            {
                if (offset >= il.Length) return false;
                value = (short)(0xFE00 | il[offset++]);
            }
            return OpCodesByValue.TryGetValue(value, out opcode);
        }

        private static bool IsMetadataOperand(OperandType operand)
        {
            return operand == OperandType.InlineField || operand == OperandType.InlineMethod ||
                   operand == OperandType.InlineTok || operand == OperandType.InlineType;
        }

        private static bool TryOperandSize(OperandType operand, byte[] il, int offset, out int size)
        {
            size = 0;
            switch (operand)
            {
                case OperandType.InlineNone: size = 0; return true;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: size = 1; return true;
                case OperandType.InlineVar: size = 2; return true;
                case OperandType.InlineBrTarget:
                case OperandType.InlineI:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.ShortInlineR: size = 4; return true;
                case OperandType.InlineI8:
                case OperandType.InlineR: size = 8; return true;
                case OperandType.InlineSwitch:
                    if (offset + 4 > il.Length) return false;
                    int count = BitConverter.ToInt32(il, offset);
                    if (count < 0 || count > (il.Length - offset - 4) / 4) return false;
                    size = 4 + (count * 4);
                    return true;
                default: return false;
            }
        }

        private static Dictionary<short, OpCode> BuildOpCodeMap()
        {
            Dictionary<short, OpCode> map = new Dictionary<short, OpCode>();
            FieldInfo[] fields = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].FieldType != typeof(OpCode)) continue;
                OpCode opcode = (OpCode)fields[i].GetValue(null);
                map[opcode.Value] = opcode;
            }
            return map;
        }
    }
}
