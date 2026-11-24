"use client";

import { Camera, ImagePlus, Loader2, X } from "lucide-react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useEffect, useId, useState } from "react";
import { toast } from "sonner";
import { FormAlert } from "@/components/auth/fields";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { send } from "@/lib/api-client";
import { MAX_BYTES, UnreadableImage, toJpeg } from "@/lib/image";
import type { Devotion, DevotionSlot } from "@/lib/types";

const MAX_TEXT = 5000;

/**
 * One slot's devotion: type it, or snap the page of a notebook, then submit (§6.3: under three taps). Submitting again edits it.
 */
export function DevotionForm({ date, slot, current }: { date: string; slot: DevotionSlot; current: Devotion | null }) {
  const t = useTranslations("devotions");
  const router = useRouter();
  const id = useId();
  const [text, setText] = useState(current?.text ?? "");
  const [photo, setPhoto] = useState<Blob | null>(null);
  const [preview, setPreview] = useState<string | null>(null);
  const [keepImage, setKeepImage] = useState(current?.hasImage ?? false);
  const [busy, setBusy] = useState<"photo" | "save" | null>(null);
  const [alert, setAlert] = useState<string | null>(null);

  useEffect(() => () => void (preview && URL.revokeObjectURL(preview)), [preview]);

  async function pick(file: File | undefined) {
    if (!file) return;
    setAlert(null);
    setBusy("photo");
    try {
      const jpeg = await toJpeg(file);
      if (jpeg.size > MAX_BYTES) throw new UnreadableImage();
      setPhoto(jpeg);
      setPreview(URL.createObjectURL(jpeg));
    } catch {
      setAlert(t("badImage"));
    } finally {
      setBusy(null);
    }
  }

  function dropPhoto() {
    setPhoto(null);
    setPreview(null);
    setKeepImage(false);
  }

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setAlert(null);
    if (!text.trim() && !photo && !keepImage) return setAlert(t("empty"));
    const form = new FormData();
    form.set("text", text);
    form.set("keepImage", String(keepImage && !photo));
    if (photo) form.set("image", photo, "devotion.jpg");
    setBusy("save");
    const r = await send<Devotion>("PUT", `/me/devotions/${date}/${slot}`, form);
    setBusy(null);
    if (r.ok) {
      toast.success(current ? t("updated") : t("submitted"));
      // The photo is now the saved one; show it as saved, not as waiting to send.
      setPhoto(null);
      setPreview(null);
      setKeepImage(r.data.hasImage);
      router.refresh();
      return;
    }
    const p = r.problem;
    setAlert(
      p.status === 0
        ? t("offline")
        : p.code === "devotion.closed"
          ? t("closed")
          : p.code === "upload.image_invalid"
            ? t("badImage")
            : p.status === 413
              ? t("tooBig")
              : p.status === 429
                ? t("slowDown")
                : t("unexpected"),
    );
  }

  const shown = preview ?? null;
  return (
    <form onSubmit={submit} noValidate className="grid gap-3">
      {alert && <FormAlert>{alert}</FormAlert>}
      <div className="grid gap-1.5">
        <Label htmlFor={`${id}-text`}>{t("textLabel")}</Label>
        <textarea
          id={`${id}-text`}
          rows={5}
          maxLength={MAX_TEXT}
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder={t("textPlaceholder")}
          aria-describedby={`${id}-count`}
          className="w-full min-w-0 rounded-lg border border-input bg-card px-3 py-2 text-base outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 dark:bg-input/30"
        />
        <p id={`${id}-count`} className="tabular text-right text-xs text-muted-foreground">
          {t("count", { count: text.length, max: MAX_TEXT })}
        </p>
      </div>

      {shown || keepImage ? (
        <div className="flex items-center gap-3 rounded-lg border border-border bg-muted/40 p-2">
          {shown ? (
            // eslint-disable-next-line @next/next/no-img-element -- a local blob preview; next/image can't optimise it
            <img src={shown} alt={t("previewAlt")} className="size-16 rounded-md object-cover" />
          ) : (
            <ImagePlus className="size-6 text-muted-foreground" aria-hidden />
          )}
          <p className="flex-1 text-sm">{shown ? t("photoReady") : t("photoSaved")}</p>
          <Button type="button" variant="ghost" size="sm" onClick={dropPhoto}>
            <X className="size-4" aria-hidden />
            {t("removePhoto")}
          </Button>
        </div>
      ) : (
        <div className="grid grid-cols-2 gap-2">
          {/* Labels styled as buttons open the camera or the gallery straight away (§11.3). */}
          <label className="flex min-h-11 cursor-pointer items-center justify-center gap-2 rounded-lg border border-border bg-card px-3 text-sm font-medium has-[:focus-visible]:ring-3 has-[:focus-visible]:ring-ring/50">
            <Camera className="size-4" aria-hidden />
            {t("takePhoto")}
            <input type="file" accept="image/*" capture="environment" className="sr-only" onChange={(e) => pick(e.target.files?.[0])} />
          </label>
          <label className="flex min-h-11 cursor-pointer items-center justify-center gap-2 rounded-lg border border-border bg-card px-3 text-sm font-medium has-[:focus-visible]:ring-3 has-[:focus-visible]:ring-ring/50">
            <ImagePlus className="size-4" aria-hidden />
            {t("choosePhoto")}
            <input type="file" accept="image/*" className="sr-only" onChange={(e) => pick(e.target.files?.[0])} />
          </label>
        </div>
      )}

      <Button type="submit" size="lg" disabled={busy !== null}>
        {busy && <Loader2 className="size-4 animate-spin" aria-hidden />}
        {busy === "photo" ? t("preparing") : busy === "save" ? t("saving") : current ? t("saveChanges") : t("submit")}
      </Button>
    </form>
  );
}
