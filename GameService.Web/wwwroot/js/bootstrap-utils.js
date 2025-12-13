window.bootstrapUtils = {
    showModal: (element) => {
        const modal = bootstrap.Modal.getOrCreateInstance(element);
        modal.show();
    },
    hideModal: (element) => {
        const modal = bootstrap.Modal.getInstance(element);
        if (modal) {
            modal.hide();
        }
    },
    showToast: (element) => {
        const toast = bootstrap.Toast.getOrCreateInstance(element);
        toast.show();
    }
};
