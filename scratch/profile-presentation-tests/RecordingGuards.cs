using System.Reflection;
using System.Reflection.Emit;
using WinPieGestures;

// Inspect the built candidate without constructing a Window or opening a modal.
// These guards establish ordering only; real recorder interaction still needs acceptance.
static class RecordingGuards
{
    record Instruction(int Offset, OpCode Code, object? Operand);
    static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(c => c.Value);

    public static void Verify(Action<bool, string> check)
    {
        var type = typeof(SettingsWindow);
        foreach (string methodName in new[] { "ProcessRawMouseButton", "ProcessRawKeyEvent" })
        {
            var instructions = Read(type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!);
            var modals = instructions.Select((value, index) => (value, index))
                .Where(x => x.value.Operand is MethodBase method && method.Name == "ShowProfileRouteError").ToArray();
            check(modals.Length == 2, methodName + " covers global and process conflict modals");
            foreach (var (modal, index) in modals)
            {
                int call = instructions.FindLastIndex(index - 1, x => x.Code == OpCodes.Call || x.Code == OpCodes.Callvirt);
                check(call >= 0 && instructions[call].Operand is MethodBase stop &&
                    stop.Name == (modal == modals[0].value ? "StopProcessTriggerRecording" : "StopTriggerRecording"),
                    methodName + " stops recording before each modal");
                var targets = instructions.SelectMany(x => x.Operand is int[] branches ? branches :
                    x.Code.FlowControl == FlowControl.Branch || x.Code.FlowControl == FlowControl.Cond_Branch ? new[] { (int)x.Operand! } : Array.Empty<int>());
                check(!targets.Any(target => target > instructions[call].Offset && target <= modal.Offset),
                    methodName + " cannot branch past recorder stop into modal");
            }
        }
        foreach (string methodName in new[] { "StopTriggerRecording", "StopProcessTriggerRecording" })
        {
            var instructions = Read(type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!);
            int increment = instructions.FindIndex(x => x.Operand is MethodBase m && m.DeclaringType == typeof(Interlocked) && m.Name == "Increment");
            int clear = instructions.FindIndex(x => x.Code == OpCodes.Stfld && x.Operand is FieldInfo f && f.Name.StartsWith("_isRecording", StringComparison.Ordinal));
            check(increment >= 0 && increment < clear, methodName + " invalidates queued recorder events before changing state");
        }
    }

    static List<Instruction> Read(MethodInfo method)
    {
        byte[] bytes = method.GetMethodBody()!.GetILAsByteArray()!;
        var result = new List<Instruction>();
        int offset = 0;
        while (offset < bytes.Length)
        {
            int start = offset;
            short value = bytes[offset++];
            if (value == 0xfe) value = unchecked((short)(0xfe00 | bytes[offset++]));
            OpCode code = Codes[value];
            object? operand = null;
            int Int32() { int n = BitConverter.ToInt32(bytes, offset); offset += 4; return n; }
            switch (code.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.InlineMethod: operand = method.Module.ResolveMethod(Int32()); break;
                case OperandType.InlineField: operand = method.Module.ResolveField(Int32()); break;
                case OperandType.ShortInlineBrTarget: operand = offset + 1 + (sbyte)bytes[offset]; offset++; break;
                case OperandType.InlineBrTarget: int delta = Int32(); operand = offset + delta; break;
                case OperandType.InlineSwitch:
                    int count = Int32(); int end = offset + count * 4;
                    operand = Enumerable.Range(0, count).Select(_ => end + Int32()).ToArray(); break;
                case OperandType.ShortInlineI: case OperandType.ShortInlineVar: offset++; break;
                case OperandType.InlineVar: offset += 2; break;
                case OperandType.InlineI8: case OperandType.InlineR: offset += 8; break;
                default: offset += 4; break;
            }
            result.Add(new Instruction(start, code, operand));
        }
        return result;
    }
}
