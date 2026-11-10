// Webcam access for WebcamDialog: shows the camera in a <video> element and takes a square snapshot.

/** Starts the camera in the video element. Returns null on success, otherwise the error name (e.g. NotAllowedError). */
export async function start(video) {
    if (!navigator.mediaDevices?.getUserMedia) return "NotSupportedError";
    try {
        const stream = await navigator.mediaDevices.getUserMedia({
            video: { facingMode: "user", width: { ideal: 1280 }, height: { ideal: 720 } },
            audio: false
        });
        video.srcObject = stream;
        await video.play();
        return null;
    } catch (e) {
        return e?.name ?? "Error";
    }
}

/** Stops the camera, so the browser's camera indicator goes off. */
export function stop(video) {
    const stream = video?.srcObject;
    if (stream) stream.getTracks().forEach(track => track.stop());
    if (video) video.srcObject = null;
}

/**
 * Takes a snapshot of the center square of the video, scaled to size x size pixels, as JPEG bytes.
 * The preview is mirrored (like a mirror); the photo is mirrored the same way, so it looks like the preview.
 */
export async function capture(video, size) {
    const width = video.videoWidth, height = video.videoHeight;
    if (!width || !height) return null;

    const side = Math.min(width, height);
    const canvas = document.createElement("canvas");
    canvas.width = size;
    canvas.height = size;
    const context = canvas.getContext("2d");
    context.translate(size, 0);
    context.scale(-1, 1);
    context.drawImage(video, (width - side) / 2, (height - side) / 2, side, side, 0, 0, size, size);

    const blob = await new Promise(resolve => canvas.toBlob(resolve, "image/jpeg", 0.9));
    return blob ? new Uint8Array(await blob.arrayBuffer()) : null;
}
