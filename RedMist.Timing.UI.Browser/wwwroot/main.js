import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = dotnetRuntime.getConfig();

await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);

export function getCurrentUrl() {
    return window.location.href;
}

// Function to get a specific query parameter by name
export function getQueryParameter(param) {
    const urlParams = new URLSearchParams(window.location.search);
    return urlParams.get(param);
}

// -------------------------------------------------------------------------------------------------
// Sharing. Called from BrowserShareSheet; every function returns one of the ShareOutcome names,
// lower-cased. The fallbacks are here rather than in C# because they are the browser's own
// facilities, and because navigator.share wants the user's click to still count as recent - a round
// trip back into managed code for each step would spend that.
// -------------------------------------------------------------------------------------------------

const PNG = "image/png";

export function canShare() {
    return typeof navigator !== "undefined" && typeof navigator.share === "function";
}

// Support is per type, so ask about the kind of file a card actually is rather than about files in
// general.
export function canShareFiles() {
    if (!canShare() || typeof navigator.canShare !== "function") return false;
    try {
        const probe = new File([new Uint8Array(1)], "probe.png", { type: PNG });
        return navigator.canShare({ files: [probe] });
    } catch {
        return false;
    }
}

export async function shareLink(title, text, url) {
    if (canShare()) {
        try {
            await navigator.share({ title, text, url });
            return "shared";
        } catch (error) {
            if (isDismissal(error)) return "dismissed";
            // Anything else - a permission refusal, an unsupported payload, a browser that claims
            // the API and then rejects - still leaves the viewer wanting the link.
        }
    }
    return await copyText(url) ? "copied" : "failed";
}

// Where the share sheet takes files this is the whole point of the feature: the image travels with
// the message instead of depending on the receiving app to fetch the link and render a preview out
// of tags a client-rendered site never serves.
//
// Otherwise the image goes to the clipboard, ready to paste into a post. Writing an image there is
// refused in more situations than reading text is - it wants a secure context, a live user gesture,
// and a browser that carries ClipboardItem - so a download is kept as the last resort. A share
// button that quietly does nothing is worse than one that hands over a file.
export async function shareImage(title, text, base64Png, fileName) {
    const file = toPngFile(base64Png, fileName);
    if (!file) return "failed";

    if (canShareFiles()) {
        try {
            await navigator.share({ files: [file], title, text });
            return "shared";
        } catch (error) {
            if (isDismissal(error)) return "dismissed";
        }
    }

    if (await copyImage(file)) return "copied";
    return download(file, fileName) ? "saved" : "failed";
}

function toPngFile(base64Png, fileName) {
    try {
        const binary = atob(base64Png);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        return new File([bytes], fileName, { type: PNG });
    } catch {
        return null;
    }
}

async function copyText(value) {
    try {
        if (!navigator.clipboard?.writeText) return false;
        await navigator.clipboard.writeText(value);
        return true;
    } catch {
        return false;
    }
}

async function copyImage(file) {
    try {
        if (typeof ClipboardItem !== "function" || !navigator.clipboard?.write) return false;
        await navigator.clipboard.write([new ClipboardItem({ [file.type]: file })]);
        return true;
    } catch {
        return false;
    }
}

function download(file, fileName) {
    let url = null;
    try {
        url = URL.createObjectURL(file);
        const link = document.createElement("a");
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        return true;
    } catch {
        return false;
    } finally {
        // Revoked on a later turn so the navigation the click started has taken the data first.
        const created = url;
        if (created) setTimeout(() => URL.revokeObjectURL(created), 10000);
    }
}

// The share sheet reports a user closing it as AbortError.
function isDismissal(error) {
    return error instanceof DOMException && error.name === "AbortError";
}
