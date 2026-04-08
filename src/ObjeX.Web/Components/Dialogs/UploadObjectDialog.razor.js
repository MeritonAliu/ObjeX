export function initDropZone(dropZone, inputFile) {
    dropZone.addEventListener("dragover", e => e.preventDefault());
    dropZone.addEventListener("drop", e => {
        e.preventDefault();
        if (e.dataTransfer?.files?.length > 0) {
            inputFile.files = e.dataTransfer.files;
            inputFile.dispatchEvent(new Event("change", { bubbles: true }));
        }
    });
}
