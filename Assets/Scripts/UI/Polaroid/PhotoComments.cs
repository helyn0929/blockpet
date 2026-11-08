using System.Collections.Generic;
using BlockPet.UI.Chat;

namespace BlockPet.UI.Polaroid
{
    /// <summary>
    /// 照片的留言＝聊天室裡回覆這張照片的訊息（回覆餵食貼文，或回覆 photo:{檔名}），所以聊天室和拍立得看到的是同一份。
    /// （原本在 PhotoSplitView，5.26 取代分割畫面時搬來這裡；留言要不要另外同步是待決 35，先照舊。）
    /// </summary>
    public static class PhotoComments
    {
        static string PhotoRef(PhotoMeta m) => "photo:" + m.fileName;

        /// <summary>聊天室裡這張照片的餵食貼文。</summary>
        public static ChatEntry PostOf(PhotoMeta m)
        {
            var fb = FirebaseManager.Instance;
            if (fb == null || m == null) return null;
            foreach (var e in fb.ChatEntries) if (e.IsPost && e.msg.photoFileName == m.fileName) return e;
            return null;
        }

        /// <summary>這張照片的留言，舊到新。</summary>
        public static List<ChatEntry> Of(PhotoMeta m)
        {
            var list = new List<ChatEntry>();
            var fb = FirebaseManager.Instance;
            if (fb == null || m == null) return list;
            string postId = PostOf(m)?.msg.messageId;
            string photoRef = PhotoRef(m);
            foreach (var e in fb.ChatEntries)
            {
                string r = e.msg.replyToMessageId;
                if (string.IsNullOrEmpty(r)) continue;
                if (r == photoRef || (!string.IsNullOrEmpty(postId) && r == postId)) list.Add(e);
            }
            return list;
        }

        /// <summary>留言（送到聊天室，回覆這張照片）。who＝拍照的人的名字（聊天室引用框用）。</summary>
        public static bool Send(PhotoMeta meta, string who, string text)
        {
            text = (text ?? "").Trim();
            var fb = FirebaseManager.Instance;
            if (text.Length == 0 || fb == null || meta == null) return false;
            var post = PostOf(meta);
            string caption = !string.IsNullOrEmpty(meta.caption) ? meta.caption : "餵了一張照片";
            var msg = new ChatMessage(fb.GetDisplayName(), text)
            {
                senderId = fb.GetUserId(),
                kind = "text",
                replyToMessageId = post != null && !string.IsNullOrEmpty(post.msg.messageId) ? post.msg.messageId : PhotoRef(meta),
                replyToDisplayName = who,
                replyToMessagePreview = "照片：" + caption,
            };
            fb.SendChatMessage(msg);
            return true;
        }
    }
}
