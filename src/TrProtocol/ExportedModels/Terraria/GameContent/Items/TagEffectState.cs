namespace Terraria.GameContent.Items;

public class TagEffectState {
    public class NetModule {
        public enum MessageType : byte
        {
            FullState,
            ChangeActiveEffect,
            ApplyTagToNPC,
            EnableProcOnNPC,
            ClearProcOnNPC
        }
    }
}
