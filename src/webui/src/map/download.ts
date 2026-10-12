/**
 * Hands the browser a file.
 *
 * The media type matters to what opens it: a .csv handed over as YAML is
 * offered to a text editor rather than to the thing the person exporting a
 * table is about to paste it into.
 */
export function save(text: string, filename: string, type = 'text/yaml'): void {
  const blob = new Blob([text], { type });
  const url = URL.createObjectURL(blob);

  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  link.click();

  // Freed on the next turn of the loop rather than immediately: revoking before
  // the click has been serviced cancels the download in some browsers.
  setTimeout(() => URL.revokeObjectURL(url), 0);
}
