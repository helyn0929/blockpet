package com.blockpet.facedetect;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;

import com.google.mlkit.vision.common.InputImage;
import com.google.mlkit.vision.face.FaceDetection;
import com.google.mlkit.vision.face.FaceDetector;
import com.google.mlkit.vision.face.FaceDetectorOptions;
import com.unity3d.player.UnityPlayer;

/**
 * 照片人臉偵測（設計文件 5.21：有人臉的照片不能寄）：ML Kit Face Detection（依賴在 Assets/Editor/Postcard/PostcardDependencies.xml）。
 * 完成後 UnitySendMessage(物件, "OnFaceResult", "檔名|1／0／-1")。
 */
public class BlockPetFaceDetect
{
    static FaceDetector detector;

    public static void detect(final Context context, final String gameObjectName, final String requestId, final String path)
    {
        try
        {
            BitmapFactory.Options opts = new BitmapFactory.Options();
            opts.inSampleSize = 2; // 偵測不用原圖那麼大
            Bitmap bmp = BitmapFactory.decodeFile(path, opts);
            if (bmp == null) { send(gameObjectName, requestId, -1); return; }
            if (detector == null)
            {
                FaceDetectorOptions o = new FaceDetectorOptions.Builder()
                        .setPerformanceMode(FaceDetectorOptions.PERFORMANCE_MODE_FAST)
                        .setMinFaceSize(0.08f)
                        .build();
                detector = FaceDetection.getClient(o);
            }
            InputImage image = InputImage.fromBitmap(bmp, 0);
            detector.process(image)
                    .addOnSuccessListener(faces -> send(gameObjectName, requestId, faces.size() > 0 ? 1 : 0))
                    .addOnFailureListener(e -> send(gameObjectName, requestId, -1));
        }
        catch (Throwable t)
        {
            send(gameObjectName, requestId, -1);
        }
    }

    static void send(String go, String id, int result)
    {
        UnityPlayer.UnitySendMessage(go, "OnFaceResult", id + "|" + result);
    }
}
