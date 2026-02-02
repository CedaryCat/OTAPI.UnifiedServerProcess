namespace Terraria.GameContent;

public static class BannerSystem
{
    public class NetBannersModule
    {
        public enum MessageType : byte
        {
            FullState,
            KillCountUpdate,
            ClaimCountUpdate,
            ClaimRequest,
            ClaimResponse
        }
    }
}
