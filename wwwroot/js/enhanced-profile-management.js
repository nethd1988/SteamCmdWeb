// Enhanced Profile Management Frontend

// Tải danh sách game profiles với tùy chọn hiển thị thông tin giải mã
async function loadGameProfiles(showDecrypted = false) {
    try {
        document.getElementById('profilesList').innerHTML = `
            <tr>
                <td colspan="8" class="text-center">
                    <div class="spinner-border text-primary" role="status">
                        <span class="visually-hidden">Đang tải...</span>
                    </div>
                    <p class="mt-2">Đang tải danh sách profile...</p>
                </td>
            </tr>
        `;

        // Use the enhanced API endpoint with decrypt parameter
        const response = await fetch(`/api/enhancedprofiles?decrypt=${showDecrypted}`);
        if (!response.ok) {
            throw new Error(`HTTP error! Status: ${response.status}`);
        }

        const data = await response.json();
        displayProfiles(data, showDecrypted);
        
        // Update profile count in UI
        const profileCountElement = document.getElementById('profileCount');
        if (profileCountElement) {
            profileCountElement.textContent = data.length;
        }
    } catch (error) {
        console.error('Error loading profiles:', error);
        document.getElementById('profilesList').innerHTML = `
            <tr>
                <td colspan="8" class="text-center text-danger">
                    <i class="bi bi-exclamation-triangle-fill me-2"></i>
                    Error loading profiles. Please try again.
                </td>
            </tr>
        `;
    }
}

// Display the profiles in the table
function displayProfiles(data, showDecrypted = false) {
    const tbody = document.getElementById('profilesList');
    tbody.innerHTML = '';

    if (!data || data.length === 0) {
        tbody.innerHTML = `
            <tr>
                <td colspan="8" class="text-center">
                    <i class="bi bi-info-circle-fill me-2 text-info"></i>
                    No profiles found. Add a new profile using the button above.
                </td>
            </tr>
        `;
        return;
    }

    // Sort by ID for consistency
    const sortedData = [...data].sort((a, b) => {
        const profileA = showDecrypted ? a.Profile : a;
        const profileB = showDecrypted ? b.Profile : b;
        return profileA.Id - profileB.Id;
    });

    sortedData.forEach(item => {
        const profile = showDecrypted ? item.Profile : item;
        const decryptedInfo = showDecrypted ? item.DecryptedInfo : null;
        
        // Định dạng trạng thái
        let statusBadge = `<span class="badge bg-secondary">Không rõ</span>`;
        if (profile.Status) {
            if (profile.Status === 'Running') {
                statusBadge = `<span class="badge bg-success">Đang chạy</span>`;
            } else if (profile.Status === 'Ready') {
                statusBadge = `<span class="badge bg-primary">Sẵn sàng</span>`;
            } else if (profile.Status === 'Stopped') {
                statusBadge = `<span class="badge bg-secondary">Đã dừng</span>`;
            } else {
                statusBadge = `<span class="badge bg-info">${profile.Status}</span>`;
            }
        }

        // Định dạng thông tin tài khoản
        let accountInfo = profile.AnonymousLogin 
            ? '<span class="badge bg-secondary">Ẩn danh</span>'
            : '<span class="badge bg-info">Có tài khoản</span>';

        // Add decrypted credentials if available
        if (showDecrypted && decryptedInfo && !profile.AnonymousLogin) {
            accountInfo = `
                <div>
                    <strong>Username:</strong> <code>${decryptedInfo.Username}</code>
                </div>
                <div class="mt-1">
                    <strong>Password:</strong> <code>${decryptedInfo.Password}</code>
                </div>
            `;
        }

        // Định dạng thời gian chạy cuối cùng
        let lastRunFormatted = 'Chưa chạy';
        if (profile.LastRun) {
            try {
                const lastRunDate = new Date(profile.LastRun);
                lastRunFormatted = lastRunDate.toLocaleString('vi-VN');
            } catch (e) {
                console.warn('Lỗi định dạng ngày tháng:', e);
                lastRunFormatted = profile.LastRun;
            }
        }

        // Truncate long paths
        const truncatedPath = profile.InstallDirectory && profile.InstallDirectory.length > 30
            ? profile.InstallDirectory.substring(0, 30) + '...'
            : (profile.InstallDirectory || 'N/A');

        const tr = document.createElement('tr');
        tr.innerHTML = `
            <td>${profile.Id}</td>
            <td class="fw-bold">${profile.Name || 'Unnamed'}</td>
            <td>${profile.AppID || 'N/A'}</td>
            <td title="${profile.InstallDirectory || ''}">${truncatedPath}</td>
            <td>${accountInfo}</td>
            <td>${statusBadge}</td>
            <td>${lastRunFormatted}</td>
            <td>
                <div class="btn-group btn-group-sm">
                    <button type="button" class="btn btn-info view-btn" data-id="${profile.Id}" title="View Details">
                        <i class="bi bi-eye"></i>
                    </button>
                    <button type="button" class="btn btn-primary edit-btn" data-id="${profile.Id}" title="Edit">
                        <i class="bi bi-pencil"></i>
                    </button>
                    <button type="button" class="btn btn-success copy-btn" data-id="${profile.Id}" title="Duplicate">
                        <i class="bi bi-files"></i>
                    </button>
                    <button type="button" class="btn btn-danger delete-btn" data-id="${profile.Id}" title="Delete">
                        <i class="bi bi-trash"></i>
                    </button>
                </div>
            </td>
        `;

        tbody.appendChild(tr);
    });

    // Add event listeners for action buttons
    addProfileActionListeners();
}

// Add event listeners for profile action buttons
function addProfileActionListeners() {
    // View button
    document.querySelectorAll('.view-btn').forEach(btn => {
        btn.addEventListener('click', function() {
            const id = parseInt(this.getAttribute('data-id'));
            viewProfileDetails(id);
        });
    });

    // Edit button
    document.querySelectorAll('.edit-btn').forEach(btn => {
        btn.addEventListener('click', function() {
            const id = parseInt(this.getAttribute('data-id'));
            editProfile(id);
        });
    });

    // Duplicate button
    document.querySelectorAll('.copy-btn').forEach(btn => {
        btn.addEventListener('click', function() {
            const id = parseInt(this.getAttribute('data-id'));
            duplicateProfile(id);
        });
    });

    // Delete button
    document.querySelectorAll('.delete-btn').forEach(btn => {
        btn.addEventListener('click', function() {
            const id = parseInt(this.getAttribute('data-id'));
            confirmDeleteProfile(id);
        });
    });
}

// View profile details with decrypted credentials
async function viewProfileDetails(id) {
    try {
        // Get profile with decrypted credentials
        const response = await fetch(`/api/enhancedprofiles/${id}?decrypt=true`);
        if (!response.ok) {
            throw new Error(`HTTP error! Status: ${response.status}`);
        }

        const result = await response.json();
        const profile = result.Profile;
        const decryptedInfo = result.DecryptedInfo;

        // Update modal with profile details
        document.getElementById('detailsModalLabel').textContent = `Profile Details: ${profile.Name}`;

        // Basic info
        document.getElementById('detail-id').textContent = profile.Id;
        document.getElementById('detail-name').textContent = profile.Name || 'Unnamed';
        document.getElementById('detail-appid').textContent = profile.AppID || 'N/A';
        document.getElementById('detail-path').textContent = profile.InstallDirectory || 'N/A';
        
        // Status with badge
        let statusHtml = profile.Status || 'Unknown';
        if (profile.Status === 'Running') {
            statusHtml = '<span class="badge bg-success">Running</span>';
        } else if (profile.Status === 'Ready') {
            statusHtml = '<span class="badge bg-primary">Ready</span>';
        } else if (profile.Status === 'Stopped') {
            statusHtml = '<span class="badge bg-secondary">Stopped</span>';
        }
        document.getElementById('detail-status').innerHTML = statusHtml;

        // Account info with decrypted credentials
        let accountHtml = '';
        if (profile.AnonymousLogin) {
            accountHtml = '<span class="badge bg-secondary">Anonymous Login</span>';
        } else if (decryptedInfo) {
            accountHtml = `
                <div class="mb-1">Username: <code>${decryptedInfo.Username}</code> <button class="btn btn-sm btn-outline-secondary copy-credential" data-credential="${decryptedInfo.Username}"><i class="bi bi-clipboard"></i></button></div>
                <div>Password: <code>${decryptedInfo.Password}</code> <button class="btn btn-sm btn-outline-secondary copy-credential" data-credential="${decryptedInfo.Password}"><i class="bi bi-clipboard"></i></button></div>
            `;
        }
        document.getElementById('detail-account').innerHTML = accountHtml;

        // Other details
        document.getElementById('detail-arguments').textContent = profile.Arguments || 'None';
        const lastRunDate = profile.LastRun ? new Date(profile.LastRun).toLocaleString() : 'Never';
        document.getElementById('detail-lastrun').textContent = lastRunDate;

        // Add copy credential button listeners
        document.querySelectorAll('.copy-credential').forEach(btn => {
            btn.addEventListener('click', function() {
                const text = this.getAttribute('data-credential');
                navigator.clipboard.writeText(text)
                    .then(() => {
                        // Change button icon temporarily to show success
                        const icon = this.querySelector('i');
                        icon.classList.remove('bi-clipboard');
                        icon.classList.add('bi-clipboard-check');
                        setTimeout(() => {
                            icon.classList.remove('bi-clipboard-check');
                            icon.classList.add('bi-clipboard');
                        }, 1500);
                    });
            });
        });

        // Set up edit and duplicate buttons
        document.getElementById('editProfileBtn').setAttribute('data-id', profile.Id);
        document.getElementById('copyProfileBtn').setAttribute('data-id', profile.Id);

        // Show the modal
        const modal = new bootstrap.Modal(document.getElementById('detailsModal'));
        modal.show();
    } catch (error) {
        console.error('Error viewing profile details:', error);
        showToast('Error loading profile details', 'error');
    }
}

// Edit a profile
function editProfile(id) {
    // Navigate to the edit page or open edit modal
    window.location.href = `/AppProfiles?edit=${id}`;
}

// Duplicate a profile
async function duplicateProfile(id) {
    try {
        // Show a loading indicator
        const loadingToast = showToast('Duplicating profile...', 'info');
        
        // Call the duplicate endpoint
        const response = await fetch(`/api/enhancedprofiles/${id}/duplicate`, {
            method: 'POST'
        });
        
        if (!response.ok) {
            throw new Error(`HTTP error! Status: ${response.status}`);
        }
        
        const result = await response.json();
        
        // Show success message
        showToast(`Profile duplicated successfully! New ID: ${result.Id}`, 'success');
        
        // Reload the profiles list
        loadGameProfiles();
    } catch (error) {
        console.error('Error duplicating profile:', error);
        showToast('Error duplicating profile', 'error');
    }
}

// Confirm profile deletion
function confirmDeleteProfile(id) {
    // Fetch the profile details to show in the confirmation dialog
    fetch(`/api/enhancedprofiles/${id}`)
        .then(response => response.json())
        .then(profile => {
            document.getElementById('deleteGameName').textContent = profile.Name || 'Unnamed Profile';
            document.getElementById('confirmDeleteBtn').setAttribute('data-id', id);
            
            // Show the modal
            const modal = new bootstrap.Modal(document.getElementById('deleteConfirmModal'));
            modal.show();
        })
        .catch(error => {
            console.error('Error fetching profile for delete confirmation:', error);
            showToast('Error preparing delete confirmation', 'error');
        });
}

// Delete a profile
function deleteProfile(id) {
    const deleteBtn = document.getElementById('confirmDeleteBtn');
    const originalText = deleteBtn.innerHTML;
    deleteBtn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status"></span> Deleting...';
    deleteBtn.disabled = true;
    
    fetch(`/api/enhancedprofiles/${id}`, {
        method: 'DELETE'
    })
    .then(response => {
        if (!response.ok) {
            throw new Error(`HTTP error! Status: ${response.status}`);
        }
        
        // Close the modal
        bootstrap.Modal.getInstance(document.getElementById('deleteConfirmModal')).hide();
        
        // Show success message
        showToast('Profile deleted successfully', 'success');
        
        // Reload the profiles list
        loadGameProfiles();
    })
    .catch(error => {
        console.error('Error deleting profile:', error);
        showToast('Error deleting profile', 'error');
    })
    .finally(() => {
        // Reset button state
        deleteBtn.innerHTML = originalText;
        deleteBtn.disabled = false;
    });
}

// Toggle decrypted credentials view
function toggleDecryptedView() {
    const showDecrypted = document.getElementById('showDecryptedToggle').checked;
    loadGameProfiles(showDecrypted);
}

// Save new or updated profile
async function saveAppProfile() {
    // Get form values
    const id = parseInt(document.getElementById('profileId').value);
    const name = document.getElementById('name').value.trim();
    const appId = document.getElementById('appId').value.trim();
    const installDirectory = document.getElementById('installDirectory').value.trim();
    const steamUsername = document.getElementById('steamUsername').value.trim();
    const steamPassword = document.getElementById('steamPassword').value.trim();
    const arguments = document.getElementById('arguments').value.trim();
    const anonymousLogin = document.getElementById('anonymousLogin').checked;
    const validateFiles = document.getElementById('validateFiles').checked;
    const autoRun = document.getElementById('autoRun').checked;
    
    // Kiểm tra các trường bắt buộc
    if (!name || !appId || !installDirectory) {
        showToast('Vui lòng điền đầy đủ các thông tin bắt buộc', 'error');
        return;
    }
    
    // Kiểm tra nếu đăng nhập ẩn danh không nên có thông tin đăng nhập
    if (anonymousLogin && (steamUsername || steamPassword)) {
        showToast('Đăng nhập ẩn danh không nên có thông tin tài khoản. Vui lòng xóa tên đăng nhập và mật khẩu hoặc bỏ chọn đăng nhập ẩn danh.', 'warning');
        return;
    }
    
    // Kiểm tra nếu không phải đăng nhập ẩn danh thì phải có thông tin đăng nhập
    if (!anonymousLogin && (!steamUsername || !steamPassword)) {
        showToast('Đăng nhập không ẩn danh yêu cầu cả tên đăng nhập và mật khẩu', 'warning');
        return;
    }
    
    // Create profile data
    const profileData = {
        id: id || 0,
        name: name,
        appID: appId,
        installDirectory: installDirectory,
        steamUsername: steamUsername,
        steamPassword: steamPassword,
        arguments: arguments,
        anonymousLogin: anonymousLogin,
        validateFiles: validateFiles,
        autoRun: autoRun,
        status: "Ready"
    };
    
    // Set loading state
    const saveBtn = document.getElementById('saveAppProfileBtn');
    const originalText = saveBtn.innerHTML;
    saveBtn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status"></span> Saving...';
    saveBtn.disabled = true;
    
    try {
        // Determine if it's a new profile or an update
        const isEditMode = id > 0;
        const url = isEditMode ? `/api/enhancedprofiles/${id}` : '/api/enhancedprofiles';
        const method = isEditMode ? 'PUT' : 'POST';
        
        const response = await fetch(url, {
            method: method,
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(profileData)
        });
        
        if (!response.ok) {
            throw new Error(`HTTP error! Status: ${response.status}`);
        }
        
        const result = await response.json();
        
        // Close the modal
        bootstrap.Modal.getInstance(document.getElementById('addAppProfileModal')).hide();
        
        // Show success message
        showToast(`Profile ${isEditMode ? 'updated' : 'created'} successfully!`, 'success');
        
        // Reload the profiles list
        loadGameProfiles();
    } catch (error) {
        console.error('Error saving profile:', error);
        showToast(`Error ${id ? 'updating' : 'creating'} profile`, 'error');
    } finally {
        // Reset button state
        saveBtn.innerHTML = originalText;
        saveBtn.disabled = false;
    }
}

// Utility function for showing toast notifications
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
    toast.classList.add('position-fixed', 'bottom-0', 'end-0', 'p-3');
    toast.style.zIndex = 1070;
    
    toast.innerHTML = `
        <div class="toast align-items-center text-white ${bgClass}" role="alert" aria-live="assertive" aria-atomic="true">
            <div class="d-flex">
                <div class="toast-body">
                    <i class="bi ${icon} me-2"></i>
                    ${message}
                </div>
                <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
            </div>
        </div>
    `;
    
    container.appendChild(toast);
    const toastEl = new bootstrap.Toast(toast.querySelector('.toast'), { delay: duration });
    toastEl.show();
    
    // Auto-remove the toast element after it's hidden
    toast.querySelector('.toast').addEventListener('hidden.bs.toast', () => {
        toast.remove();
    });
    
    return toastEl;
}

// Reset the add/edit profile form
function resetAppProfileForm() {
    const form = document.getElementById('appProfileForm');
    form.reset();
    document.getElementById('profileId').value = 0;
    document.getElementById('addAppProfileModalLabel').textContent = 'Add New Game Profile';
    
    // Update form for anonymous login state
    updateCredentialFields();
}

// Update the credential fields based on anonymous login checkbox
function updateCredentialFields() {
    const anonymousLogin = document.getElementById('anonymousLogin').checked;
    const usernameField = document.getElementById('steamUsername');
    const passwordField = document.getElementById('steamPassword');
    const credentialsSection = document.getElementById('credentialsSection');
    
    if (anonymousLogin) {
        usernameField.value = '';
        passwordField.value = '';
        credentialsSection.classList.add('d-none');
    } else {
        credentialsSection.classList.remove('d-none');
    }
}

// Document ready function
document.addEventListener('DOMContentLoaded', function() {
    // Load profiles on page load
    loadGameProfiles();
    
    // Set up event listeners
    document.getElementById('refreshBtn')?.addEventListener('click', function() {
        loadGameProfiles(document.getElementById('showDecryptedToggle')?.checked || false);
    });
    
    document.getElementById('showDecryptedToggle')?.addEventListener('change', toggleDecryptedView);
    
    // Add profile button
    document.querySelector('[data-bs-target="#addAppProfileModal"]')?.addEventListener('click', resetAppProfileForm);
    
    // Save profile button
    document.getElementById('saveAppProfileBtn')?.addEventListener('click', saveAppProfile);
    
    // Confirm delete button
    document.getElementById('confirmDeleteBtn')?.addEventListener('click', function() {
        const id = parseInt(this.getAttribute('data-id'));
        deleteProfile(id);
    });
    
    // Edit profile button in details modal
    document.getElementById('editProfileBtn')?.addEventListener('click', function() {
        const id = parseInt(this.getAttribute('data-id'));
        editProfile(id);
    });
    
    // Duplicate profile button in details modal
    document.getElementById('copyProfileBtn')?.addEventListener('click', function() {
        const id = parseInt(this.getAttribute('data-id'));
        duplicateProfile(id);
        // Close the modal
        bootstrap.Modal.getInstance(document.getElementById('detailsModal')).hide();
    });
    
    // Anonymous login toggle event
    document.getElementById('anonymousLogin')?.addEventListener('change', updateCredentialFields);
});