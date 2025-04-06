// Tiện ích chung cho SteamCmdWeb

// Khởi tạo tooltips
document.addEventListener('DOMContentLoaded', function () {
    // Khởi tạo tất cả các tooltips
    const tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
    const tooltipList = tooltipTriggerList.map(function (tooltipTriggerEl) {
        return new bootstrap.Tooltip(tooltipTriggerEl);
    });

    // Khởi tạo toasts
    const toastElList = [].slice.call(document.querySelectorAll('.toast'));
    const toastList = toastElList.map(function (toastEl) {
        return new bootstrap.Toast(toastEl);
    });
});

// Hiển thị toast thông báo
function showToast(message, type = 'success', duration = 5000) {
    const container = document.getElementById('toastContainer') || document.body;
    const toastId = 'toast-' + Date.now();
    
    let bgClass = 'bg-success';
    let icon = 'bi-check-circle-fill';
    
    if (type === 'error') {
        bgClass = 'bg-danger';
        icon = 'bi-exclamation-triangle-fill';
    } else if (type === 'warning') {
        bgClass = 'bg-warning';
        icon = 'bi-exclamation-circle-fill';
    } else if (type === 'info') {
        bgClass = 'bg-info';
        icon = 'bi-info-circle-fill';
    }
    
    const toast = document.createElement('div');
    toast.classList.add('toast', 'fade-in');
    toast.setAttribute('id', toastId);
    toast.setAttribute('role', 'alert');
    toast.setAttribute('aria-live', 'assertive');
    toast.setAttribute('aria-atomic', 'true');
    
    toast.innerHTML = `
        <div class="toast-header ${bgClass} text-white">
            <i class="bi ${icon} me-2"></i>
            <strong class="me-auto">Thông báo</strong>
            <small>Vừa xong</small>
            <button type="button" class="btn-close btn-close-white" data-bs-dismiss="toast" aria-label="Close"></button>
        </div>
        <div class="toast-body">
            ${message}
        </div>
    `;
    
    container.appendChild(toast);
    
    const bsToast = new bootstrap.Toast(toast, {
        delay: duration
    });
    
    bsToast.show();
    
    // Tự động xóa toast sau khi ẩn
    toast.addEventListener('hidden.bs.toast', function () {
        toast.remove();
    });
    
    return bsToast;
}

// Cắt ngắn văn bản quá dài
function truncateText(text, maxLength) {
    if (!text) return '';
    return text.length > maxLength ? text.substring(0, maxLength) + '...' : text;
}

// Format date từ ISO string
function formatDate(dateString) {
    if (!dateString) return 'N/A';
    
    try {
        const date = new Date(dateString);
        return date.toLocaleString('vi-VN');
    } catch (e) {
        console.warn('Lỗi định dạng ngày:', e);
        return dateString;
    }
}

// Copy text to clipboard
function copyToClipboard(text) {
    return navigator.clipboard.writeText(text)
        .then(() => {
            showToast('Đã sao chép vào clipboard', 'success');
            return true;
        })
        .catch(err => {
            console.error('Lỗi khi sao chép vào clipboard:', err);
            showToast('Không thể sao chép vào clipboard', 'error');
            return false;
        });
}

// Gửi request lên server với xử lý lỗi
async function fetchWithErrorHandling(url, options = {}) {
    try {
        const response = await fetch(url, options);
        
        if (!response.ok) {
            const errorData = await response.json().catch(() => null);
            throw new Error(errorData?.message || `HTTP error! Status: ${response.status}`);
        }
        
        return await response.json();
    } catch (error) {
        console.error('Fetch error:', error);
        showToast(error.message || 'Đã xảy ra lỗi khi gửi yêu cầu', 'error');
        throw error;
    }
}

// Chọn tất cả checkboxes trong bảng
function toggleSelectAll(tableId, checked) {
    const table = document.getElementById(tableId);
    if (!table) return;
    
    const checkboxes = table.querySelectorAll('input[type="checkbox"]');
    checkboxes.forEach(checkbox => {
        checkbox.checked = checked;
    });
}

// Tạo hiệu ứng loading cho button
function setButtonLoading(button, isLoading, originalText = null) {
    if (isLoading) {
        const loadingText = button.getAttribute('data-loading-text') || 'Đang xử lý...';
        button.setAttribute('data-original-text', button.innerHTML);
        button.innerHTML = `<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> ${loadingText}`;
        button.disabled = true;
    } else {
        button.innerHTML = originalText || button.getAttribute('data-original-text') || button.innerHTML;
        button.disabled = false;
        button.removeAttribute('data-original-text');
    }
}