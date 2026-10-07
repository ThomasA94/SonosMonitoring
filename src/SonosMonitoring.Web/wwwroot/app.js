window.sonosTimeZone = () => Intl.DateTimeFormat().resolvedOptions().timeZone;

window.sonosDownload = bytes => {
    const url = URL.createObjectURL(new Blob([bytes], { type: 'application/json' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = 'sonosmonitoring-snapshot.json';
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
};
