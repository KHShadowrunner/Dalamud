using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dalamud.Game.Chat.SeStringHandling.Payloads
{
    public class ItemPayload : Payload
    {
        public override PayloadType Type => PayloadType.Item;

        public uint ItemId { get; private set; }
        public string ItemName { get; private set; } = string.Empty;
        public bool IsHQ { get; private set; } = false;

        public ItemPayload() { }

        public ItemPayload(uint itemId, bool isHQ)
        {
            ItemId = itemId;
            IsHQ = isHQ;
        }

        public override void Resolve()
        {
            if (string.IsNullOrEmpty(ItemName))
            {
                dynamic item = XivApi.GetItem((int)ItemId).GetAwaiter().GetResult();
                ItemName = item.Name;
            }
        }

        public override byte[] Encode()
        {
            var actualItemId = IsHQ ? ItemId + 1000000 : ItemId;
            var idBytes = MakeInteger(actualItemId);
            bool hasName = !string.IsNullOrEmpty(ItemName);

            var chunkLen = idBytes.Length + 4;
            if (hasName)
            {
                // 1 additional unknown byte compared to the nameless version, 1 byte for the name length, and then the name itself
                chunkLen += (1 + 1 + ItemName.Length);
                if (IsHQ)
                {
                    chunkLen += 4;  // unicode representation of the HQ symbol is 3 bytes, preceded by a space
                }
            }

            var bytes = new List<byte>()
            {
                START_BYTE,
                (byte)SeStringChunkType.Interactable, (byte)chunkLen, (byte)EmbeddedInfoType.ItemLink
            };
            bytes.AddRange(idBytes);
            // unk
            bytes.AddRange(new byte[] { 0x02, 0x01 });

            // Links don't have to include the name, but if they do, it requires additional work
            if (hasName)
            {
                var nameLen = ItemName.Length + 1;
                if (IsHQ)
                {
                    nameLen += 4;   // space plus 3 bytes for HQ symbol
                }

                bytes.AddRange(new byte[]
                {
                    0xFF,   // unk
                    (byte)nameLen
                });
                bytes.AddRange(Encoding.UTF8.GetBytes(ItemName));

                if (IsHQ)
                {
                    // space and HQ symbol
                    bytes.AddRange(new byte[] { 0x20, 0xEE, 0x80, 0xBC });
                }
            }

            bytes.Add(END_BYTE);

            return bytes.ToArray();
        }

        public override string ToString()
        {
            return $"{Type} - ItemId: {ItemId}, ItemName: {ItemName}, IsHQ: {IsHQ}";
        }

        protected override void ProcessChunkImpl(BinaryReader reader, long endOfStream)
        {
            ItemId = GetInteger(reader);

            if (ItemId > 1000000)
            {
                ItemId -= 1000000;
                IsHQ = true;
            }

            if (reader.BaseStream.Position + 3 < endOfStream)
            {
                // unk
                reader.ReadBytes(3);

                var itemNameLen = (int)GetInteger(reader);
                var itemNameBytes = reader.ReadBytes(itemNameLen);

                // HQ items have the HQ symbol as part of the name, but since we already recorded
                // the HQ flag, we want just the bare name
                if (IsHQ)
                {
                    itemNameBytes = itemNameBytes.Take(itemNameLen - 4).ToArray();
                }

                ItemName = Encoding.UTF8.GetString(itemNameBytes);
            }
        }

        protected override byte GetMarkerForIntegerBytes(byte[] bytes)
        {
            // custom marker just for hq items?
            if (bytes.Length == 3 && IsHQ)
            {
                return (byte)IntegerType.Int24Special;
            }

            return base.GetMarkerForIntegerBytes(bytes);
        }
    }
}
