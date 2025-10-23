window.downloadFile = (fileName, contentBase64) => {
    const a = document.createElement('a');
    a.href = "data:text/json;base64," + contentBase64;
    a.download = fileName;
    a.click();
    a.remove();
};