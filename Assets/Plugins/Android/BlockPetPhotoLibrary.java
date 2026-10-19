package com.blockpet.photolib;

import android.content.ContentResolver;
import android.content.ContentUris;
import android.content.Context;
import android.database.Cursor;
import android.net.Uri;
import android.os.Build;
import android.provider.MediaStore;

import com.unity3d.player.UnityPlayer;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;

/**
 * 取得相簿最新的相機照片（DCIM 底下、排除截圖），複製到 app 快取資料夾，
 * 完成後用 UnitySendMessage 把結果（JSON）傳回 C# 的 PhotoLibrary。
 * 複製原始檔（不重新編碼）以保留 EXIF 旋轉資訊，由 NativeGallery.LoadImageAtPath 處理方向與縮圖。
 */
public class BlockPetPhotoLibrary
{
    public static void fetchRecentPhotos(final Context context, final String gameObjectName, final int limit, final long sinceUnixSeconds)
    {
        new Thread(new Runnable()
        {
            @Override
            public void run()
            {
                String result = "{\"items\":[]}";
                try { result = query(context, limit, sinceUnixSeconds); }
                catch (Exception e) { android.util.Log.e("BlockPetPhotoLibrary", "fetch failed", e); }
                UnityPlayer.UnitySendMessage(gameObjectName, "OnNativePhotosFetched", result);
            }
        }).start();
    }

    static String query(Context context, int limit, long sinceUnixSeconds) throws Exception
    {
        ContentResolver resolver = context.getContentResolver();
        Uri collection = MediaStore.Images.Media.EXTERNAL_CONTENT_URI;

        String pathColumn = Build.VERSION.SDK_INT >= 29
                ? MediaStore.Images.Media.RELATIVE_PATH
                : MediaStore.Images.Media.DATA;

        String[] projection = {
                MediaStore.Images.Media._ID,
                MediaStore.Images.Media.DATE_TAKEN,
                MediaStore.Images.Media.DATE_ADDED,
        };

        // 相機照片都在 DCIM 底下；部分機型（如 Samsung）截圖放在 DCIM/Screenshots，另外排除
        String selection = MediaStore.Images.Media.DATE_ADDED + " >= ? AND "
                + pathColumn + " LIKE ? AND "
                + pathColumn + " NOT LIKE ?";
        String[] args = {
                String.valueOf(sinceUnixSeconds),
                Build.VERSION.SDK_INT >= 29 ? "DCIM/%" : "%/DCIM/%",
                "%Screenshot%",
        };
        String sort = MediaStore.Images.Media.DATE_ADDED + " DESC";

        File dir = new File(context.getCacheDir(), "blockpet_hand");
        if (!dir.exists()) dir.mkdirs();

        JSONArray items = new JSONArray();
        Cursor cursor = resolver.query(collection, projection, selection, args, sort);
        if (cursor != null)
        {
            try
            {
                int idCol = cursor.getColumnIndexOrThrow(MediaStore.Images.Media._ID);
                int takenCol = cursor.getColumnIndexOrThrow(MediaStore.Images.Media.DATE_TAKEN);
                int addedCol = cursor.getColumnIndexOrThrow(MediaStore.Images.Media.DATE_ADDED);

                while (cursor.moveToNext() && items.length() < limit)
                {
                    long id = cursor.getLong(idCol);
                    long takenMs = cursor.isNull(takenCol) ? 0 : cursor.getLong(takenCol);
                    if (takenMs <= 0) takenMs = cursor.getLong(addedCol) * 1000L;

                    Uri uri = ContentUris.withAppendedId(collection, id);
                    File out = new File(dir, id + ".img");
                    if (!out.exists() && !copy(resolver, uri, out)) continue;

                    JSONObject item = new JSONObject();
                    item.put("id", String.valueOf(id));
                    item.put("path", out.getAbsolutePath());
                    item.put("takenAtMs", takenMs);
                    items.put(item);
                }
            }
            finally { cursor.close(); }
        }

        JSONObject root = new JSONObject();
        root.put("items", items);
        return root.toString();
    }

    static boolean copy(ContentResolver resolver, Uri uri, File out)
    {
        File tmp = new File(out.getAbsolutePath() + ".tmp");
        try (InputStream in = resolver.openInputStream(uri); OutputStream os = new FileOutputStream(tmp))
        {
            if (in == null) throw new java.io.IOException("openInputStream returned null");
            byte[] buf = new byte[64 * 1024];
            int n;
            while ((n = in.read(buf)) > 0) os.write(buf, 0, n);
        }
        catch (Exception e)
        {
            tmp.delete();
            return false;
        }
        return tmp.renameTo(out);
    }
}
