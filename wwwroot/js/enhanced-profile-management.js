// Enhanced Profile Management Frontend

// Tải danh sách game profiles với tùy chọn hiển thị thông tin giải mã
async function loadGameProfiles(showDecrypted = false) {
    // Thay đổi từ dòng 5
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

        // Thay đổi từ /api/enhancedprofiles thành /api/profiles
        const response = await fetch(`/api/profiles?decrypt=${showDecrypted}`);
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

// Thay đổi trong hàm viewProfileDetails (dòng 160)
async function viewProfileDetails(id) {
    try {
        // Thay đổi từ /api/enhancedprofiles thành /api/profiles
        const response = await fetch(`/api/profiles/${id}?decrypt=true`);
        if (!response.ok) {
            throw new Error(`HTTP error! Status: ${response.status}`);
        }

        // Phần còn lại giữ nguyên
    } catch (error) {
        console.error('Error viewing profile details:', error);
        showToast('Error loading profile details', 'error');
    }
}

// Thay đổi trong hàm duplicateProfile (dòng 256)
async function duplicateProfile(id) {
    try {
        // Show a loading indicator
        const loadingToast = showToast('Duplicating profile...', 'info');
        
        // Thay đổi từ /api/enhancedprofiles thành /api/profiles
        const response = await fetch(`/api/profiles/${id}/duplicate`, {
            method: 'POST'
        });
        
        // Phần còn lại giữ nguyên
    } catch (error) {
        console.error('Error duplicating profile:', error);
        showToast('Error duplicating profile', 'error');
    }
}

// Thay đổi trong hàm confirmDeleteProfile (dòng 282)
function confirmDeleteProfile(id) {
    // Thay đổi từ /api/enhancedprofiles thành /api/profiles
    fetch(`/api/profiles/${id}`)
        .then(response => response.json())
        .then(profile => {
            // Phần còn lại giữ nguyên
        })
        .catch(error => {
            console.error('Error fetching profile for delete confirmation:', error);
            showToast('Error preparing delete confirmation', 'error');
        });
}

// Thay đổi trong hàm deleteProfile (dòng 298)
function deleteProfile(id) {
    const deleteBtn = document.getElementById('confirmDeleteBtn');
    const originalText = deleteBtn.innerHTML;
    deleteBtn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status"></span> Deleting...';
    deleteBtn.disabled = true;
    
    // Thay đổi từ /api/enhancedprofiles thành /api/profiles
    fetch(`/api/profiles/${id}`, {
        method: 'DELETE'
    })
    .then(response => {
        // Phần còn lại giữ nguyên
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

// Thay đổi trong hàm saveAppProfile (dòng 354)
async function saveAppProfile() {
    // ... phần đầu giữ nguyên ...
    
    try {
        // Determine if it's a new profile or an update
        const isEditMode = id > 0;
        // Thay đổi từ /api/enhancedprofiles thành /api/profiles
        const url = isEditMode ? `/api/profiles/${id}` : '/api/profiles';
        const method = isEditMode ? 'PUT' : 'POST';
        
        // Phần còn lại giữ nguyên
    } catch (error) {
        console.error('Error saving profile:', error);
        showToast(`Error ${id ? 'updating' : 'creating'} profile`, 'error');
    } finally {
        // Reset button state
        saveBtn.innerHTML = originalText;
        saveBtn.disabled = false;
    }
}