# CropPNG

CropPNG is a lightweight and super-fast C# program for cropping PNG/JPG/GIF/BMP images with minimal overhead. It leverages **BmpPixelSnoop** for efficient pixel manipulation.

## Features
- Overwrites the original file by default.
- Optionally saves the cropped image with a `_Crop` suffix.
- Accepts file paths without requiring quotes.
- Retains the original modified date of the cropped image.

## Usage
```
croppng [/n] filepath [filepath]
```
### Parameters
- `/n` – Saves the cropped image with `_Crop` appended to the filename instead of overwriting the original.
- `filepath` – One or more file paths of PNG images to be cropped.

### Example Commands
```sh
croppng image.png  # Overwrites the original image.png
croppng /n image.png  # Saves the cropped image as image_Crop.png
croppng image1.png image2.png  # Crops multiple images
```

## Performance
CropPNG uses **BmpPixelSnoop**, making it an ultra-efficient and lightweight cropping tool without unnecessary overhead.

## License
[MIT License](LICENSE)

## Author
Created by Craig White.
