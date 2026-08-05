using System;
using System.Globalization;
using RainbowMage.OverlayPlugin.NetworkProcessors.PacketHelper;

namespace RainbowMage.OverlayPlugin.NetworkProcessors
{
    class LineActorCastExtra : LineBaseSubMachina<LineActorCastExtra.ActorCastExtraPacket>
    {
        public const uint LogFileLineID = 263;
        public const string LogLineName = "ActorCastExtra";
        public const string MachinaPacketName = "ActorCast";

        internal class ActorCastExtraPacket : MachinaPacketWrapper
        {
            public override string ToString(long epoch, uint ActorID)
            {
                UInt16 abilityId = Get<UInt16>("ActionID");

                // for x/y/x, subtract 7FFF then divide by (2^15 - 1) / 100
                float x = FFXIVRepository.ConvertUInt16Coordinate(Get<UInt16>("PosX"));
                // In-game uses Y as elevation and Z as north-south, but ACT convention is to use
                // Z as elevation and Y as north-south.
                float y = FFXIVRepository.ConvertUInt16Coordinate(Get<UInt16>("PosZ"));
                float z = FFXIVRepository.ConvertUInt16Coordinate(Get<UInt16>("PosY"));
                // Global packets encode rotation as UInt16, while CN/KR/TW Machina
                // packet structs expose the already-normalized in-game radians as Single.
                // Reading every region as UInt16 throws for every CN ActorCast packet and
                // silently drops the 0x107/263 ActorCastExtra log line.
                var rotation = packetType.GetField("Rotation")?.GetValue(packetValue)
                    ?? throw new InvalidOperationException("ActorCast packet has no Rotation field");
                double h = ConvertRotation(rotation);

                return string.Format(CultureInfo.InvariantCulture,
                    "{0:X8}|{1:X4}|{2:F3}|{3:F3}|{4:F3}|{5:F3}",
                    ActorID, abilityId, x, y, z, h);
            }

            internal static double ConvertRotation(object rotation) => rotation switch
            {
                UInt16 encoded => FFXIVRepository.ConvertHeading(encoded),
                Single radians => radians,
                _ => throw new InvalidOperationException(
                    $"Unsupported ActorCast Rotation field type {rotation.GetType().FullName}"),
            };
        }
        public LineActorCastExtra(TinyIoCContainer container)
            : base(container, LogFileLineID, LogLineName, MachinaPacketName) { }
    }
}
