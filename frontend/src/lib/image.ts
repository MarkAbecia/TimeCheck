// Devotion photos are shrunk and re-encoded in the browser before upload (§11.3): faster on mobile data, upright, and
// HEIC from iPhones becomes JPEG (the server re-encodes again and never trusts this step).
export const MAX_SIDE = 2048;
export const MAX_BYTES = 10 * 1024 * 1024;

export class UnreadableImage extends Error {}

export async function toJpeg(file: File, maxSide = MAX_SIDE, quality = 0.85): Promise<Blob> {
  let bitmap: ImageBitmap;
  try {
    bitmap = await createImageBitmap(file, { imageOrientation: "from-image" });
  } catch {
    throw new UnreadableImage(); // e.g. HEIC on a browser that can't decode it
  }
  const scale = Math.min(1, maxSide / Math.max(bitmap.width, bitmap.height));
  const canvas = document.createElement("canvas");
  canvas.width = Math.round(bitmap.width * scale);
  canvas.height = Math.round(bitmap.height * scale);
  canvas.getContext("2d")!.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
  bitmap.close();
  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, "image/jpeg", quality));
  if (!blob) throw new UnreadableImage();
  return blob;
}
