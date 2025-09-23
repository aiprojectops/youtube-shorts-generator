window.downloadFile = async (url, filename) => {
    try {
        const response = await fetch(url);
        const blob = await response.blob();

        const link = document.createElement('a');
        link.href = URL.createObjectURL(blob);
        link.download = filename;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);

        // 메모리 정리
        URL.revokeObjectURL(link.href);

        return true;
    } catch (error) {
        console.error('다운로드 실패:', error);
        return false;
    }
};