/**
 * SURVEYWEB - PREMIUM INTERACTIONS
 * Micro-interactions and animations for enhanced UX
 */

(function() {
    'use strict';

    // Wait for DOM to be ready
    document.addEventListener('DOMContentLoaded', function() {
        
        // ===== Smooth Scroll =====
        document.querySelectorAll('a[href^="#"]').forEach(anchor => {
            anchor.addEventListener('click', function(e) {
                const href = this.getAttribute('href');
                if (href !== '#' && href !== '#!') {
                    e.preventDefault();
                    const target = document.querySelector(href);
                    if (target) {
                        target.scrollIntoView({
                            behavior: 'smooth',
                            block: 'start'
                        });
                    }
                }
            });
        });

        // ===== Card Hover Effects =====
        const cards = document.querySelectorAll('.survey-card-premium, .card-premium');
        cards.forEach(card => {
            card.addEventListener('mouseenter', function() {
                this.style.transition = 'all 0.3s cubic-bezier(0.4, 0, 0.2, 1)';
            });
        });

        // ===== Button Ripple Effect =====
        const buttons = document.querySelectorAll('.btn-premium');
        buttons.forEach(button => {
            button.addEventListener('click', function(e) {
                const ripple = document.createElement('span');
                const rect = this.getBoundingClientRect();
                const size = Math.max(rect.width, rect.height);
                const x = e.clientX - rect.left - size / 2;
                const y = e.clientY - rect.top - size / 2;

                ripple.style.width = ripple.style.height = size + 'px';
                ripple.style.left = x + 'px';
                ripple.style.top = y + 'px';
                ripple.classList.add('ripple-effect');

                this.appendChild(ripple);

                setTimeout(() => {
                    ripple.remove();
                }, 600);
            });
        });

        // ===== Scroll Animations =====
        const observerOptions = {
            threshold: 0.1,
            rootMargin: '0px 0px -50px 0px'
        };

        const observer = new IntersectionObserver(function(entries) {
            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    entry.target.style.opacity = '1';
                    entry.target.style.transform = 'translateY(0)';
                    observer.unobserve(entry.target);
                }
            });
        }, observerOptions);

        // Animate cards on scroll
        document.querySelectorAll('.survey-card-premium').forEach((card, index) => {
            card.style.opacity = '0';
            card.style.transform = 'translateY(30px)';
            card.style.transition = `opacity 0.6s ease ${index * 0.1}s, transform 0.6s ease ${index * 0.1}s`;
            observer.observe(card);
        });

        // ===== Search Input Focus Effect =====
        const searchInputs = document.querySelectorAll('.search-premium input, .form-control-premium');
        searchInputs.forEach(input => {
            input.addEventListener('focus', function() {
                this.parentElement.style.transform = 'scale(1.02)';
                this.parentElement.style.transition = 'transform 0.2s ease';
            });

            input.addEventListener('blur', function() {
                this.parentElement.style.transform = 'scale(1)';
            });
        });

        // ===== Chip Filter Animation =====
        const chips = document.querySelectorAll('.chip-premium');
        chips.forEach(chip => {
            chip.addEventListener('click', function(e) {
                // Add pulse animation
                this.style.animation = 'pulse 0.5s ease';
                setTimeout(() => {
                    this.style.animation = '';
                }, 500);
            });
        });

        // ===== Navbar Scroll Effect =====
        let lastScroll = 0;
        const navbar = document.querySelector('.navbar-premium');
        
        if (navbar) {
            window.addEventListener('scroll', function() {
                const currentScroll = window.pageYOffset;

                if (currentScroll > 100) {
                    navbar.style.boxShadow = '0 4px 6px -1px rgb(0 0 0 / 0.1), 0 2px 4px -2px rgb(0 0 0 / 0.1)';
                    navbar.style.background = 'rgba(255, 255, 255, 0.98)';
                } else {
                    navbar.style.boxShadow = '0 1px 3px 0 rgb(0 0 0 / 0.1), 0 1px 2px -1px rgb(0 0 0 / 0.1)';
                    navbar.style.background = 'rgba(255, 255, 255, 0.95)';
                }

                lastScroll = currentScroll;
            });
        }

        // ===== Loading State for Buttons =====
        const formButtons = document.querySelectorAll('button[type="submit"]');
        formButtons.forEach(button => {
            const form = button.closest('form');
            if (form) {
                form.addEventListener('submit', function(e) {
                    if (this.checkValidity()) {
                        button.disabled = true;
                        const originalHTML = button.innerHTML;
                        button.innerHTML = '<i class="bi bi-hourglass-split"></i> Đang xử lý...';
                        
                        // Re-enable after 5 seconds (failsafe)
                        setTimeout(() => {
                            button.disabled = false;
                            button.innerHTML = originalHTML;
                        }, 5000);
                    }
                });
            }
        });

        // ===== Toast Notifications =====
        function showToast(message, type = 'success') {
            const toast = document.createElement('div');
            toast.className = `toast-notification toast-${type}`;
            toast.innerHTML = `
                <div class="toast-icon">
                    <i class="bi bi-${type === 'success' ? 'check-circle' : type === 'error' ? 'x-circle' : 'info-circle'}"></i>
                </div>
                <div class="toast-message">${message}</div>
            `;
            
            document.body.appendChild(toast);

            setTimeout(() => {
                toast.classList.add('toast-show');
            }, 10);

            setTimeout(() => {
                toast.classList.remove('toast-show');
                setTimeout(() => toast.remove(), 300);
            }, 3000);
        }

        // ===== Form Validation Feedback =====
        const inputs = document.querySelectorAll('.form-control-premium');
        inputs.forEach(input => {
            input.addEventListener('invalid', function(e) {
                e.preventDefault();
                this.style.borderColor = 'var(--color-danger)';
                this.style.animation = 'shake 0.5s';
                
                setTimeout(() => {
                    this.style.animation = '';
                }, 500);
            });

            input.addEventListener('input', function() {
                if (this.validity.valid) {
                    this.style.borderColor = 'var(--color-success)';
                } else {
                    this.style.borderColor = '';
                }
            });
        });

        // ===== Page Transition Effect =====
        window.addEventListener('beforeunload', function() {
            document.body.style.opacity = '0';
            document.body.style.transition = 'opacity 0.3s ease';
        });

        // Fade in on page load
        document.body.style.opacity = '0';
        setTimeout(() => {
            document.body.style.transition = 'opacity 0.5s ease';
            document.body.style.opacity = '1';
        }, 10);

        // ===== Back to Top Button =====
        const backToTop = document.createElement('button');
        backToTop.className = 'back-to-top';
        backToTop.innerHTML = '<i class="bi bi-arrow-up"></i>';
        backToTop.style.cssText = `
            position: fixed;
            bottom: 30px;
            right: 30px;
            width: 50px;
            height: 50px;
            border-radius: 50%;
            background: var(--gradient-primary);
            color: white;
            border: none;
            cursor: pointer;
            opacity: 0;
            visibility: hidden;
            transition: all 0.3s ease;
            z-index: 1000;
            box-shadow: var(--shadow-xl);
        `;

        document.body.appendChild(backToTop);

        window.addEventListener('scroll', function() {
            if (window.pageYOffset > 300) {
                backToTop.style.opacity = '1';
                backToTop.style.visibility = 'visible';
            } else {
                backToTop.style.opacity = '0';
                backToTop.style.visibility = 'hidden';
            }
        });

        backToTop.addEventListener('click', function() {
            window.scrollTo({
                top: 0,
                behavior: 'smooth'
            });
        });

        backToTop.addEventListener('mouseenter', function() {
            this.style.transform = 'translateY(-5px)';
        });

        backToTop.addEventListener('mouseleave', function() {
            this.style.transform = 'translateY(0)';
        });

        // ===== Keyboard Navigation Enhancement =====
        document.addEventListener('keydown', function(e) {
            // Escape key to close modals/dropdowns
            if (e.key === 'Escape') {
                const activeElement = document.activeElement;
                if (activeElement) {
                    activeElement.blur();
                }
            }
        });

        // ===== Dynamic placeholder animation =====
        const placeholderInputs = document.querySelectorAll('input[placeholder]');
        placeholderInputs.forEach(input => {
            const originalPlaceholder = input.getAttribute('placeholder');
            let index = 0;
            let isDeleting = false;

            function animatePlaceholder() {
                if (!document.activeElement || document.activeElement !== input) {
                    if (!isDeleting && index < originalPlaceholder.length) {
                        input.setAttribute('placeholder', originalPlaceholder.substring(0, index + 1));
                        index++;
                        setTimeout(animatePlaceholder, 100);
                    }
                }
            }

            input.addEventListener('focus', function() {
                this.setAttribute('placeholder', originalPlaceholder);
                index = originalPlaceholder.length;
            });

            // Start animation on page load
            if (input.classList.contains('search-premium')) {
                input.setAttribute('placeholder', '');
                index = 0;
                setTimeout(animatePlaceholder, 500);
            }
        });

        console.log('✨ Premium interactions loaded successfully!');
    });

})();

// ===== Add Custom CSS for interactions =====
const style = document.createElement('style');
style.textContent = `
    .ripple-effect {
        position: absolute;
        border-radius: 50%;
        background: rgba(255, 255, 255, 0.6);
        pointer-events: none;
        transform: scale(0);
        animation: ripple-animation 0.6s ease-out;
    }

    @keyframes ripple-animation {
        to {
            transform: scale(4);
            opacity: 0;
        }
    }

    @keyframes shake {
        0%, 100% { transform: translateX(0); }
        10%, 30%, 50%, 70%, 90% { transform: translateX(-10px); }
        20%, 40%, 60%, 80% { transform: translateX(10px); }
    }

    @keyframes pulse {
        0%, 100% { transform: scale(1); }
        50% { transform: scale(1.05); }
    }

    .toast-notification {
        position: fixed;
        top: 20px;
        right: 20px;
        background: white;
        padding: 1rem 1.5rem;
        border-radius: 12px;
        box-shadow: var(--shadow-xl);
        display: flex;
        align-items: center;
        gap: 1rem;
        z-index: 10000;
        transform: translateX(400px);
        transition: transform 0.3s ease;
    }

    .toast-notification.toast-show {
        transform: translateX(0);
    }

    .toast-notification.toast-success {
        border-left: 4px solid var(--color-success);
    }

    .toast-notification.toast-error {
        border-left: 4px solid var(--color-danger);
    }

    .toast-notification.toast-info {
        border-left: 4px solid var(--color-info);
    }

    .toast-icon {
        font-size: 1.5rem;
    }

    .toast-success .toast-icon {
        color: var(--color-success);
    }

    .toast-error .toast-icon {
        color: var(--color-danger);
    }

    .toast-info .toast-icon {
        color: var(--color-info);
    }
`;
document.head.appendChild(style);

