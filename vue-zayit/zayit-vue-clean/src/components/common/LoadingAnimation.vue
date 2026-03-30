<template>
  <div class="spinner-wrap">
    <div class="dots"><span /><span /><span /></div>
  </div>
</template>

<style scoped>
.spinner-wrap {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
  width: 100%;
}

.dots {
  display: flex;
  gap: 6px;
  align-items: center;
}

.dots span {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--accent-color);
  animation: bounce 1.2s ease-in-out infinite;
}

.dots span:nth-child(2) {
  animation-delay: 0.2s;
}
.dots span:nth-child(3) {
  animation-delay: 0.4s;
}

@keyframes bounce {
  0%,
  80%,
  100% {
    transform: scale(0.6);
    opacity: 0.4;
  }
  40% {
    transform: scale(1);
    opacity: 1;
  }
}
</style>
